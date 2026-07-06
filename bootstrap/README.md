# Bootstrap — one-time setup

These steps run **once** by a human with Owner/Editor on the project. After this,
every push to `master` deploys automatically via GitHub Actions — no scripts.

All commands are PowerShell. Replace the placeholders first:

```powershell
$ProjectId   = "your-gcp-project-id"
$Region      = "europe-west1"
$StateBucket = "your-tf-state-bucket"     # globally-unique
$AppBucket   = "your-app-bucket-name"
$Repo        = "your-org/comicdownloader"  # owner/repo on GitHub
gcloud config set project $ProjectId
```

## 1. Create the Terraform state bucket

This bucket holds remote state and is intentionally **not** managed by Terraform.

```powershell
gcloud storage buckets create "gs://$StateBucket" --project $ProjectId --location $Region --uniform-bucket-level-access
gcloud storage buckets update "gs://$StateBucket" --versioning
```

## 2. Initialise Terraform

```powershell
cd ../infra
copy terraform.tfvars.example terraform.tfvars   # then edit it
terraform init -backend-config="bucket=$StateBucket"
```

## 3. Create the Artifact Registry repo first (so the first image has a home)

A full apply would try to create the Cloud Run service, which needs a real
image. Break the chicken-and-egg by creating just the registry (and APIs) first.
The `image` variable is required, so pass any placeholder for this targeted run:

```powershell
terraform apply -target=google_project_service.apis -target=google_artifact_registry_repository.containers -var="image=placeholder"
```

## 4. Build and push the first image

```powershell
gcloud auth configure-docker "$Region-docker.pkg.dev" --quiet
$RepoPath = "$ProjectId/comicdownloader/comicdownloader"
dotnet publish ../comicdownloader.fsproj --configuration Release -t:PublishContainer -p:ContainerRegistry="$Region-docker.pkg.dev" -p:ContainerRepository="$RepoPath" -p:ContainerImageTag="bootstrap"
$Digest = gcloud artifacts docker images describe "$Region-docker.pkg.dev/$RepoPath:bootstrap" --format='value(image_summary.digest)'
$Image  = "$Region-docker.pkg.dev/$RepoPath@$Digest"
```

## 5. Full apply (creates everything else)

```powershell
terraform apply -var="image=$Image"
```

## 6. Seed the secret values

Terraform created the empty secret *containers*; the values live only here, never
in state. Adjust the JSON shapes to match what the app expects.

```powershell
'{"ClientId":"...","RefreshToken":"..."}' | gcloud secrets versions add comicdownloader-settings-onedrive --data-file=-
'{"ApiKey":"...","User":"..."}'           | gcloud secrets versions add comicdownloader-settings-telegram --data-file=-
```

> The app rotates the OneDrive refresh token by adding new versions at runtime.
> Terraform does not manage versions, so it will never overwrite them.

## 7. Grant the CI deployer access to the state bucket

The state bucket is outside Terraform, so grant the deployer SA explicitly:

```powershell
$Deployer = terraform output -raw deployer_service_account
gcloud storage buckets add-iam-policy-binding "gs://$StateBucket" --member="serviceAccount:$Deployer" --role="roles/storage.objectAdmin"
```

## 8. Configure GitHub repository variables

Read the Terraform outputs and set them as **repository variables** (Settings →
Secrets and variables → Actions → Variables). These are not secrets.

```powershell
terraform output -raw workload_identity_provider   # -> WIF_PROVIDER
terraform output -raw deployer_service_account      # -> DEPLOYER_SA
```

| Variable          | Value                                      |
| ----------------- | ------------------------------------------ |
| `GCP_PROJECT_ID`  | your project id                            |
| `WIF_PROVIDER`    | `workload_identity_provider` output        |
| `DEPLOYER_SA`     | `deployer_service_account` output          |
| `TF_STATE_BUCKET` | your state bucket name                     |
| `APP_BUCKET_NAME` | your app bucket name                       |

## Done

Push to `master` and the workflow builds the image, pins it by digest, and runs
`terraform apply`. The old `setup-gcp.ps1` / `deploy-gcp.ps1` scripts have been
removed — deployment is now fully automated.
