# =============================================================================
# backend.tf
# -----------------------------------------------------------------------------
# Configures where Terraform stores its state file: a Google Cloud Storage
# bucket (remote state). Remote state lets CI and multiple operators share a
# single source of truth and provides state locking so two `apply` runs can't
# corrupt each other.
# =============================================================================

# Remote state in GCS. The bucket is created once during bootstrap (see
# ../bootstrap/README.md) and is NOT managed by this configuration, to avoid a
# chicken-and-egg problem.
#
# The bucket name is intentionally omitted here so it can be supplied at init
# time without committing an environment-specific value:
#
#   terraform init -backend-config="bucket=YOUR_TF_STATE_BUCKET"
#
# GitHub Actions passes the same flag (see ../.github/workflows/deploy.yml).
terraform {
  backend "gcs" {
    prefix = "comicdownloader/state"
  }
}
