# Comic Downloader

Comic Downloader is an F# ASP.NET Core service that downloads comics on a schedule, stores downloaded images in Google Cloud Storage, keeps secrets and configuration in Secret Manager, uploads new images to OneDrive, and sends them to Telegram.

The service emits Cloud Run-friendly structured JSON logs and OpenTelemetry traces for inbound requests, job execution, and outgoing HTTP calls.

## Endpoints

- `GET /health` returns a simple health response.
- `POST /jobs/download` runs the comic download workflow.
- `POST /jobs/refresh-onedrive` refreshes the stored OneDrive refresh token.

These job endpoints are intended to be invoked by Cloud Scheduler through an authenticated Cloud Run service.

## Build

```powershell
dotnet build
```

## Publish Container Image

```powershell
dotnet publish .\comicdownloader.fsproj -t:PublishContainer -p:ContainerArchiveOutputPath=bin/Release/net10.0/comicdownloader-image.tar.gz
```

## Infrastructure and Deployment

Infrastructure is managed with Terraform under [`infra/`](infra/), and deployment
is fully automated with GitHub Actions ([`.github/workflows/deploy.yml`](.github/workflows/deploy.yml)).
Every push to `master` builds the container image, pins it by digest, and runs
`terraform apply` to update Cloud Run, IAM, Secret Manager containers, and the
two Cloud Scheduler jobs.

GitHub Actions authenticates to GCP keylessly via Workload Identity Federation —
no service-account key is stored.

### First-time setup

The one-time bootstrap (state bucket, initial image, secret seeding, GitHub
repository variables) is documented in [`bootstrap/README.md`](bootstrap/README.md).
After bootstrap, deployments require no local scripts.

## Configuration

The service expects Google Application Default Credentials and these environment variables:

- `GCS_BUCKET_NAME`: bucket used for downloaded comic images and dedupe markers.
- `GCP_PROJECT_ID` or `GOOGLE_CLOUD_PROJECT`: GCP project containing the Secret Manager secrets.
- `COMICDOWNLOADER_SECRET_PREFIX`: optional secret name prefix. Defaults to `comicdownloader-`.
- `CALVIN_HOBBES_SOURCE_BUCKET`: optional private GCS bucket containing scanned Calvin and Hobbes source images.
- `CALVIN_HOBBES_SOURCE_PREFIX`: optional object prefix inside the Calvin and Hobbes source bucket. Defaults to `sources/calvin-and-hobbes/`.
- `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT`: optional OTLP endpoint for exporting OpenTelemetry traces from Cloud Run.

## Observability

- Logs are written as single-line JSON using `Google.Cloud.Logging.Console`, so Cloud Run forwards them to Cloud Logging with `logging.googleapis.com/trace`, `logging.googleapis.com/spanId`, and `logging.googleapis.com/trace_sampled` when request activity is present.
- Traces are created with OpenTelemetry ASP.NET Core and HTTP client instrumentation, plus an internal span around each `/jobs/*` execution.
- OTLP trace export is enabled automatically when `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` or `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

## Secret Layout

Secrets are resolved from Secret Manager using this pattern:

- `comicdownloader-settings-onedrive`
- `comicdownloader-settings-telegram`

If `COMICDOWNLOADER_SECRET_PREFIX` is set, it replaces the `comicdownloader-` prefix in those names.

`settings-onedrive` is stored as JSON and the refresh workflow writes a new secret version whenever the refresh token rotates.

## Calvin And Hobbes Source

The downloader can optionally pull Calvin and Hobbes from a private GCS bucket instead of a public feed.

- Only objects whose file name matches `calvin_xxxx.png`, `calvin_xxxx.jpg`, or `calvin_xxxx.jpeg` are considered.
- The downloader selects the lexicographically newest matching object under `CALVIN_HOBBES_SOURCE_PREFIX`.
- Existing hash-based dedupe still applies, so reprocessing the same image does not resend it.

Example upload command:

```powershell
gcloud storage cp .\calvin\* gs://YOUR_SOURCE_BUCKET/sources/calvin-and-hobbes/
```

To enable the source, set `calvin_hobbes_source_bucket` (and optionally
`calvin_hobbes_source_prefix`) in `infra/terraform.tfvars`. Terraform creates the
bucket, grants the runtime service account read access, and wires the
`CALVIN_HOBBES_SOURCE_BUCKET` / `CALVIN_HOBBES_SOURCE_PREFIX` environment
variables into the Cloud Run service.

## Object Layout

Downloaded comic images are stored in GCS as normalized object paths:

- `<comic-name>/<hash>`

Those same objects are used as the dedupe markers, so an existing object means the comic has already been processed.