# =============================================================================
# variables.tf
# -----------------------------------------------------------------------------
# Declares every input the configuration accepts. Variables without a `default`
# are mandatory and must be supplied (via -var, a tfvars file, or CI). Those
# with a default are optional overrides. Several variables are deliberately
# coupled to runtime app behaviour or to "empty string = feature off" toggles;
# those non-obvious couplings are called out below.
# =============================================================================

variable "project_id" {
  type        = string
  description = "GCP project ID that hosts the service."
}

variable "region" {
  type        = string
  description = "Region for Cloud Run, Artifact Registry, and buckets."
  default     = "europe-west1"
}

variable "service_name" {
  type        = string
  description = "Cloud Run service name. Also used as the Scheduler job prefix."
  default     = "comicdownloader"
}

variable "artifact_registry_repository" {
  type        = string
  description = "Artifact Registry Docker repository ID for container images."
  default     = "comicdownloader"
}

variable "app_bucket_name" {
  type        = string
  description = "GCS bucket for downloaded comic images and dedupe markers."
}

# Empty string is a sentinel: it switches the whole Calvin and Hobbes feature
# off. When empty, the source bucket, its IAM binding, and the related env vars
# are all skipped (see count/conditional logic in storage.tf, iam.tf, cloud_run.tf).
variable "calvin_hobbes_source_bucket" {
  type        = string
  description = "Optional private GCS bucket holding Calvin and Hobbes source images. Empty string disables it."
  default     = ""
}

variable "calvin_hobbes_source_prefix" {
  type        = string
  description = "Object prefix inside the Calvin and Hobbes source bucket."
  default     = "sources/calvin-and-hobbes/"
}

# Non-obvious coupling: this prefix is how the app finds its secrets at runtime.
# It is both used to name the Secret Manager secrets (secrets.tf) AND passed to
# the container as COMICDOWNLOADER_SECRET_PREFIX (cloud_run.tf). The two must
# agree or the app looks up secret names that don't exist.
variable "secret_prefix" {
  type        = string
  description = "Secret name prefix. Must match COMICDOWNLOADER_SECRET_PREFIX in the app."
  default     = "comicdownloader-"
}

# Empty string sentinel again: when blank, the OTLP env var is omitted entirely
# so the app exports no traces (rather than exporting to a broken endpoint).
variable "otlp_traces_endpoint" {
  type        = string
  description = "Optional OTLP traces endpoint. Empty string disables OTLP export."
  default     = ""
}

variable "download_schedule" {
  type        = string
  description = "Cron schedule for the comic download job."
  default     = "0 */5 * * *"
}

variable "refresh_schedule" {
  type        = string
  description = "Cron schedule for the OneDrive token refresh job."
  default     = "0 0 * * *"
}

variable "scheduler_time_zone" {
  type        = string
  description = "IANA time zone for the Cloud Scheduler jobs."
  default     = "Etc/UTC"
}

# Pinning by digest (image@sha256:...) rather than a mutable tag (:latest) makes
# deploys deterministic: Terraform only rolls out a new Cloud Run revision when
# the digest actually changes, and the deployed code is unambiguous.
variable "image" {
  type        = string
  description = "Fully-qualified container image to deploy, ideally pinned by digest. Supplied by CI."
}

variable "github_repository" {
  type        = string
  description = "GitHub repository in 'owner/repo' form, allowed to authenticate via Workload Identity Federation."
}
