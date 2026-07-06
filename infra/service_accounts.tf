# =============================================================================
# service_accounts.tf
# -----------------------------------------------------------------------------
# Declares the three distinct service-account identities used by the system,
# following least-privilege: each SA does one job and is granted only the
# permissions for that job in iam.tf. Keeping runtime, scheduler, and CI as
# separate identities means a compromise of one doesn't grant the others' rights.
# =============================================================================

# Identity the Cloud Run service runs as.
resource "google_service_account" "runtime" {
  account_id   = "comicdownloader-runtime"
  display_name = "Comic Downloader Runtime"

  depends_on = [google_project_service.apis]
}

# Identity Cloud Scheduler uses to invoke the authenticated Cloud Run service.
resource "google_service_account" "scheduler" {
  account_id   = "comicdownloader-scheduler"
  display_name = "Comic Downloader Scheduler"

  depends_on = [google_project_service.apis]
}

# Identity GitHub Actions impersonates (via Workload Identity Federation) to
# build, push, and apply Terraform.
resource "google_service_account" "deployer" {
  account_id   = "comicdownloader-deployer"
  display_name = "Comic Downloader CI Deployer"

  depends_on = [google_project_service.apis]
}
