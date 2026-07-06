# =============================================================================
# scheduler.tf
# -----------------------------------------------------------------------------
# Creates the Cloud Scheduler cron jobs that drive the app. There is no
# always-on worker: each job is just an authenticated HTTP POST to a Cloud Run
# endpoint on a schedule. One job triggers comic downloads, the other refreshes
# the OneDrive token. Both authenticate as the scheduler SA via OIDC.
# =============================================================================

locals {
  # The Cloud Run-assigned HTTPS URL. Read from the service resource so the jobs
  # always target the current deployment without hard-coding a URL.
  service_url = google_cloud_run_v2_service.service.uri
}

# Periodic comic-download trigger.
resource "google_cloud_scheduler_job" "download" {
  name      = "${var.service_name}-download"
  region    = var.region
  schedule  = var.download_schedule
  time_zone = var.scheduler_time_zone

  http_target {
    http_method = "POST"
    # Path-specific endpoint the app exposes for this job.
    uri = "${local.service_url}/jobs/download"

    # Makes Scheduler mint a Google-signed OIDC identity token for each call so
    # the private Cloud Run service accepts it. Without this the request is
    # unauthenticated and rejected with 403.
    oidc_token {
      service_account_email = google_service_account.scheduler.email
      # audience must equal the service URL: the token is minted for this
      # audience and Cloud Run validates the claim matches itself.
      audience = local.service_url
    }
  }

  depends_on = [
    google_project_service.apis,
    # Don't create the job before the scheduler SA can actually invoke the
    # service, otherwise the first scheduled run would fail with a 403.
    google_cloud_run_v2_service_iam_member.scheduler_invoker,
  ]
}

# Daily OneDrive token-refresh trigger. Same auth pattern, different endpoint
# and schedule. This job is why the runtime SA needs secretVersionAdder (iam.tf):
# refreshing rotates the token by writing a new secret version.
resource "google_cloud_scheduler_job" "refresh_onedrive" {
  name      = "${var.service_name}-refresh-onedrive"
  region    = var.region
  schedule  = var.refresh_schedule
  time_zone = var.scheduler_time_zone

  http_target {
    http_method = "POST"
    uri         = "${local.service_url}/jobs/refresh-onedrive"

    oidc_token {
      service_account_email = google_service_account.scheduler.email
      audience              = local.service_url
    }
  }

  depends_on = [
    google_project_service.apis,
    google_cloud_run_v2_service_iam_member.scheduler_invoker,
  ]
}
