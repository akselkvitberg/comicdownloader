# =============================================================================
# monitoring.tf
# -----------------------------------------------------------------------------
# Emails an alert when a comic download fails. Two sources feed one policy:
#   * ERROR logs from the service. Every per-comic failure is logged at ERROR by
#     Download.downloadComic, as is any job that throws (Program.fs).
#   * Cloud Scheduler attempts that fail (non-2xx, timeout, auth), which catch the
#     job not running at all, where the app itself never gets to log anything.
# Failures are also sent to Telegram by the app, but that path depends on the
# app being healthy and Telegram settings loading; this one does not.
#
# Off when alert_email is empty (same sentinel pattern as other optional
# features), so a plan without the variable stays a no-op.
# =============================================================================

locals {
  alerts_enabled = var.alert_email != ""
}

resource "google_monitoring_notification_channel" "email" {
  count = local.alerts_enabled ? 1 : 0

  display_name = "${var.service_name} alerts"
  type         = "email"
  labels = {
    email_address = var.alert_email
  }

  depends_on = [google_project_service.apis]
}

resource "google_monitoring_alert_policy" "download_failures" {
  count = local.alerts_enabled ? 1 : 0

  display_name = "${var.service_name}: comic download failures"
  combiner     = "OR"

  conditions {
    display_name = "Error logged or scheduled job failed"

    condition_matched_log {
      filter = <<-EOT
        (resource.type="cloud_run_revision"
         AND resource.labels.service_name="${var.service_name}"
         AND severity>=ERROR)
        OR
        (resource.type="cloud_scheduler_job"
         AND resource.labels.job_id=~"^${var.service_name}-"
         AND severity>=ERROR)
      EOT

      # Surfaces which comic failed in the incident and the email. Empty for
      # errors not tied to a comic (e.g. a scheduler failure).
      label_extractors = {
        comic = "EXTRACT(jsonPayload.format_parameters.ComicName)"
      }
    }
  }

  alert_strategy {
    # The download job runs every 5 hours, so a broken source errors on every
    # run. One email per hour at most keeps a multi-comic outage to one mail.
    notification_rate_limit {
      period = "3600s"
    }
    auto_close = "1800s"
  }

  documentation {
    mime_type = "text/markdown"
    content   = <<-EOT
      A comic download failed (comic: `$${log.extracted_label.comic}`).

      Recent errors:
      `gcloud logging read 'resource.labels.service_name="${var.service_name}" AND severity>=ERROR' --project=${var.project_id} --freshness=1d`
    EOT
  }

  notification_channels = [google_monitoring_notification_channel.email[0].id]

  depends_on = [google_project_service.apis]
}
