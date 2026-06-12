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

## Bootstrap GCP Locally

Use the setup script to create the bucket, create or update the required secrets, and print the env vars for local execution:

```powershell
.\setup-gcp.ps1 -ProjectId YOUR_PROJECT_ID -BucketName YOUR_BUCKET_NAME
```

You can also pass secret values directly when bootstrapping:

```powershell
.\setup-gcp.ps1 \
	-ProjectId YOUR_PROJECT_ID \
	-BucketName YOUR_BUCKET_NAME \
	-CalvinHobbesSourceBucket YOUR_SOURCE_BUCKET \
	-OneDriveClientId YOUR_ONEDRIVE_CLIENT_ID \
	-OneDriveRefreshToken YOUR_ONEDRIVE_REFRESH_TOKEN \
	-TelegramApiKey YOUR_TELEGRAM_BOT_KEY \
	-TelegramUser YOUR_TELEGRAM_USER
```

## Deploy to Cloud Run

Use the deployment script to publish the SDK-built container image to Artifact Registry, deploy Cloud Run, grant the runtime and scheduler IAM bindings, and create or update the two Cloud Scheduler jobs:

```powershell
.\deploy-gcp.ps1 -ProjectId YOUR_PROJECT_ID -BucketName YOUR_BUCKET_NAME
```

Useful options:

```powershell
.\deploy-gcp.ps1 \
	-ProjectId YOUR_PROJECT_ID \
	-BucketName YOUR_BUCKET_NAME \
	-Region europe-west1 \
	-ArtifactRegistryRepository comicdownloader \
	-ServiceName comicdownloader \
	-SecretPrefix comicdownloader- \
	-OtlpTracesEndpoint https://YOUR_COLLECTOR_OR_BACKEND:4317
```

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

Example deployment with the source enabled:

```powershell
.\deploy-gcp.ps1 \
	-ProjectId YOUR_PROJECT_ID \
	-BucketName YOUR_APP_BUCKET \
	-CalvinHobbesSourceBucket YOUR_SOURCE_BUCKET \
	-CalvinHobbesSourcePrefix sources/calvin-and-hobbes/
```

## Object Layout

Downloaded comic images are stored in GCS as normalized object paths:

- `<comic-name>/<hash>`

Those same objects are used as the dedupe markers, so an existing object means the comic has already been processed.