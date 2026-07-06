# =============================================================================
# versions.tf
# -----------------------------------------------------------------------------
# Pins the Terraform CLI and provider versions, and configures the default
# Google Cloud provider. Establishes the baseline every other file relies on:
# a consistent toolchain and a project/region that all google_* resources
# inherit unless they override it.
# =============================================================================

terraform {
  # Floor, not a ceiling. Any CLI >= 1.6.0 is accepted; this guards against
  # running on older versions that lack syntax/features used in this config.
  required_version = ">= 1.6.0"

  required_providers {
    google = {
      source = "hashicorp/google"
      # Pessimistic constraint: allows 6.x but blocks the 7.0 major bump, which
      # could introduce breaking changes. Keeps `terraform init` reproducible.
      version = "~> 6.0"
    }
  }
}

# Default provider configuration. Every google_* resource that doesn't set its
# own project/region inherits these values, so they only need to be defined once.
provider "google" {
  project = var.project_id
  region  = var.region
}
