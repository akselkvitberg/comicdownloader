[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectId,

    [Parameter(Mandatory = $true)]
    [string]$BucketName,

    [string]$Region = "europe-west1",

    [string]$ArtifactRegistryRepository = "comicdownloader",

    [string]$ServiceName = "comicdownloader",

    [string]$ImageTag = "latest",

    [string]$RuntimeServiceAccountName = "comicdownloader-runtime",

    [string]$SchedulerInvokerServiceAccountName = "comicdownloader-scheduler",

    [string]$SecretPrefix = "comicdownloader-",

    [string]$DownloadSchedule = "0 */5 * * *",

    [string]$RefreshSchedule = "0 0 * * *",

    [string]$Location = "europe-west1",

    [switch]$SkipBuild,

    [switch]$AllowUnauthenticated
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Invoke-GCloud {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & gcloud @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "gcloud command failed: gcloud $($Arguments -join ' ')"
    }
}

function Test-GCloud {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & gcloud @Arguments 2>$null 1>$null
    $LASTEXITCODE -eq 0
}

function Ensure-ServiceAccount {
    param(
        [string]$AccountName,
        [string]$DisplayName
    )

    $email = "$AccountName@$ProjectId.iam.gserviceaccount.com"

    if (-not (Test-GCloud iam service-accounts describe $email --project $ProjectId)) {
        Invoke-GCloud iam service-accounts create $AccountName --display-name $DisplayName --project $ProjectId
    }

    $email
}

function Ensure-ArtifactRegistryRepository {
    if (-not (Test-GCloud artifacts repositories describe $ArtifactRegistryRepository --location $Region --project $ProjectId)) {
        Invoke-GCloud artifacts repositories create $ArtifactRegistryRepository --repository-format docker --location $Region --description "Comic Downloader container images" --project $ProjectId
    }
}

function Publish-ContainerImage {
    param([string]$ImageName)

    $registryHost = "$Region-docker.pkg.dev"

    Invoke-GCloud auth configure-docker $registryHost --quiet

    & dotnet publish .\comicdownloader.fsproj `
        --configuration Release `
        -t:PublishContainer `
        "-p:ContainerRegistry=$registryHost" `
        "-p:ContainerRepository=$ProjectId/$ArtifactRegistryRepository/$ServiceName" `
        "-p:ContainerImageTag=$ImageTag"

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet container publish failed for image '$ImageName'"
    }
}

function Ensure-ProjectRoleBinding {
    param(
        [string]$Member,
        [string]$Role
    )

    Invoke-GCloud projects add-iam-policy-binding $ProjectId --member $Member --role $Role --quiet
}

function Ensure-BucketRoleBinding {
    param(
        [string]$Member,
        [string]$Role
    )

    Invoke-GCloud storage buckets add-iam-policy-binding "gs://$BucketName" --member $Member --role $Role
}

function Ensure-SchedulerJob {
    param(
        [string]$JobName,
        [string]$Schedule,
        [string]$Uri,
        [string]$ServiceAccountEmail
    )

    $args = @(
        "scheduler", "jobs", "create", "http", $JobName,
        "--project", $ProjectId,
        "--location", $Location,
        "--schedule", $Schedule,
        "--uri", $Uri,
        "--http-method", "POST",
        "--oidc-service-account-email", $ServiceAccountEmail,
        "--oidc-token-audience", $Uri
    )

    if (Test-GCloud scheduler jobs describe $JobName --location $Location --project $ProjectId) {
        $args[2] = "update"
    }

    Invoke-GCloud @args
}

Require-Command gcloud
Require-Command dotnet

Invoke-GCloud config set project $ProjectId
Ensure-ArtifactRegistryRepository

$imageName = "$Region-docker.pkg.dev/$ProjectId/$ArtifactRegistryRepository/$ServiceName`:$ImageTag"

if (-not $SkipBuild) {
    Publish-ContainerImage -ImageName $imageName
}

$runtimeServiceAccount = Ensure-ServiceAccount -AccountName $RuntimeServiceAccountName -DisplayName "Comic Downloader Runtime"
$schedulerServiceAccount = Ensure-ServiceAccount -AccountName $SchedulerInvokerServiceAccountName -DisplayName "Comic Downloader Scheduler"

Ensure-ProjectRoleBinding -Member "serviceAccount:$runtimeServiceAccount" -Role "roles/secretmanager.secretAccessor"
Ensure-BucketRoleBinding -Member "serviceAccount:$runtimeServiceAccount" -Role "roles/storage.objectAdmin"

$deployArgs = @(
    "run", "deploy", $ServiceName,
    "--project", $ProjectId,
    "--region", $Region,
    "--image", $imageName,
    "--service-account", $runtimeServiceAccount,
    "--set-env-vars", "GCP_PROJECT_ID=$ProjectId,GCS_BUCKET_NAME=$BucketName,COMICDOWNLOADER_SECRET_PREFIX=$SecretPrefix",
    "--no-allow-unauthenticated"
)

if ($AllowUnauthenticated) {
    $deployArgs = $deployArgs | Where-Object { $_ -ne "--no-allow-unauthenticated" }
    $deployArgs += "--allow-unauthenticated"
}

Invoke-GCloud @deployArgs

$serviceUrl = (& gcloud run services describe $ServiceName --project $ProjectId --region $Region --format "value(status.url)").Trim()
if (-not $serviceUrl) {
    throw "Failed to resolve deployed service URL."
}

Invoke-GCloud run services add-iam-policy-binding $ServiceName --project $ProjectId --region $Region --member "serviceAccount:$schedulerServiceAccount" --role "roles/run.invoker"

Ensure-SchedulerJob -JobName "$ServiceName-download" -Schedule $DownloadSchedule -Uri "$serviceUrl/jobs/download" -ServiceAccountEmail $schedulerServiceAccount
Ensure-SchedulerJob -JobName "$ServiceName-refresh-onedrive" -Schedule $RefreshSchedule -Uri "$serviceUrl/jobs/refresh-onedrive" -ServiceAccountEmail $schedulerServiceAccount

Write-Host ""
Write-Host "Deployment complete."
Write-Host "Service URL: $serviceUrl"
Write-Host "Image: $imageName"
Write-Host "Scheduler jobs: $ServiceName-download, $ServiceName-refresh-onedrive"