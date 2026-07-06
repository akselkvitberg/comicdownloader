# =============================================================================
# apis.tf
# -----------------------------------------------------------------------------
# Enables the Google Cloud APIs that the rest of the configuration depends on.
# A fresh GCP project has almost everything disabled; resource creation fails
# with a "service not enabled" error until the matching API is on. Nearly every
# other resource declares `depends_on = [google_project_service.apis]` so this
# runs first.
# =============================================================================

locals {
  # Each entry maps to a feature used elsewhere: run (Cloud Run), artifactregistry
  # (image repo), secretmanager (secrets), cloudscheduler (cron jobs), storage
  # (buckets), and the iam/sts/iamcredentials trio plus resourcemanager &
  # serviceusage that Workload Identity Federation and IAM management require.
  required_apis = [
    "run.googleapis.com",
    "artifactregistry.googleapis.com",
    "secretmanager.googleapis.com",
    "cloudscheduler.googleapis.com",
    "storage.googleapis.com",
    "iam.googleapis.com",
    "iamcredentials.googleapis.com",
    "sts.googleapis.com",
    "cloudresourcemanager.googleapis.com",
    "serviceusage.googleapis.com",
  ]
}

# Creates one enablement resource per API in the list (for_each over the set).
resource "google_project_service" "apis" {
  for_each = toset(local.required_apis)

  project = var.project_id
  service = each.value

  # Important: don't disable the API when the resource is destroyed. Other
  # workloads in the same project may rely on these APIs; turning them off on a
  # `terraform destroy` could break unrelated services. Safer to leave them on.
  disable_on_destroy = false
}
