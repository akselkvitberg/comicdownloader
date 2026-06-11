[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectId,

    [Parameter(Mandatory = $true)]
    [string]$BucketName,

    [string]$Location = "europe-west1",

    [string]$SecretPrefix = "comicdownloader-",

    [string]$VgCookie,

    [string]$OneDriveClientId,

    [string]$OneDriveRefreshToken,

    [string]$TelegramApiKey,

    [string]$TelegramUser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Get-SecretName {
    param([string]$Suffix)

    "$SecretPrefix$Suffix"
}

function Ensure-Secret {
    param(
        [string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        Write-Host "Skipping secret '$Name' because no value was provided."
        return
    }

    $exists = gcloud secrets describe $Name --project $ProjectId 2>$null

    if ($LASTEXITCODE -ne 0) {
        $Value | gcloud secrets create $Name --project $ProjectId --data-file=- | Out-Null
        Write-Host "Created secret '$Name'."
        return
    }

    $Value | gcloud secrets versions add $Name --project $ProjectId --data-file=- | Out-Null
    Write-Host "Added new version to secret '$Name'."
}

Require-Command gcloud

gcloud config set project $ProjectId | Out-Null

$bucketUri = "gs://$BucketName"
$bucketExists = gcloud storage buckets describe $bucketUri --project $ProjectId 2>$null

if ($LASTEXITCODE -ne 0) {
    gcloud storage buckets create $bucketUri --project $ProjectId --location $Location | Out-Null
    Write-Host "Created bucket '$bucketUri' in '$Location'."
}
else {
    Write-Host "Bucket '$bucketUri' already exists."
}

Ensure-Secret -Name (Get-SecretName "settings-vgcookie") -Value $VgCookie

if (-not [string]::IsNullOrWhiteSpace($OneDriveClientId) -and -not [string]::IsNullOrWhiteSpace($OneDriveRefreshToken)) {
    $oneDrivePayload = @{
        ClientId = $OneDriveClientId
        RefreshToken = $OneDriveRefreshToken
    } | ConvertTo-Json -Compress

    Ensure-Secret -Name (Get-SecretName "settings-onedrive") -Value $oneDrivePayload
}
else {
    Write-Host "Skipping OneDrive secret because client id and refresh token were not both provided."
}

if (-not [string]::IsNullOrWhiteSpace($TelegramApiKey) -and -not [string]::IsNullOrWhiteSpace($TelegramUser)) {
    $telegramPayload = @{
        ApiKey = $TelegramApiKey
        User = $TelegramUser
    } | ConvertTo-Json -Compress

    Ensure-Secret -Name (Get-SecretName "settings-telegram") -Value $telegramPayload
}
else {
    Write-Host "Skipping Telegram secret because API key and user were not both provided."
}

Write-Host ""
Write-Host "Local runtime configuration:"
Write-Host ('$env:GCP_PROJECT_ID="{0}"' -f $ProjectId)
Write-Host ('$env:GCS_BUCKET_NAME="{0}"' -f $BucketName)

if (-not [string]::IsNullOrWhiteSpace($SecretPrefix) -and $SecretPrefix -ne "comicdownloader-") {
    Write-Host ('$env:COMICDOWNLOADER_SECRET_PREFIX="{0}"' -f $SecretPrefix)
}

Write-Host 'go run .'