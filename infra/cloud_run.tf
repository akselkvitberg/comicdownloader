# =============================================================================
# cloud_run.tf
# -----------------------------------------------------------------------------
# Deploys the application itself as a Cloud Run (v2) service: the container that
# Cloud Scheduler calls to download comics and refresh tokens. Assembles the
# container's environment from required + feature-gated optional variables, runs
# it as the least-privilege runtime SA, and waits for IAM so the app can reach
# its secrets and bucket on first boot.
# =============================================================================

locals {
  # Always-present environment passed to the container.
  base_env = {
    GCP_PROJECT_ID  = var.project_id
    GCS_BUCKET_NAME = var.app_bucket_name
    # Must match the prefix used to name the secrets in secrets.tf, or the app
    # will look up secret names that don't exist.
    COMICDOWNLOADER_SECRET_PREFIX = var.secret_prefix
  }

  # Feature-gated env vars. Each block returns {} (contributing nothing) when its
  # feature is disabled, or the relevant vars when enabled. merge() folds them
  # together so disabled features leave no trace in the container config.
  optional_env = merge(
    var.calvin_hobbes_source_bucket == "" ? {} : {
      CALVIN_HOBBES_SOURCE_BUCKET = var.calvin_hobbes_source_bucket
      CALVIN_HOBBES_SOURCE_PREFIX = var.calvin_hobbes_source_prefix
    },
    var.otlp_traces_endpoint == "" ? {} : {
      OTEL_EXPORTER_OTLP_TRACES_ENDPOINT = var.otlp_traces_endpoint
    },
  )

  # Final env map handed to the container below. Later keys win in merge(), but
  # there is no overlap here so base and optional simply combine.
  env = merge(local.base_env, local.optional_env)
}

resource "google_cloud_run_v2_service" "service" {
  name     = var.service_name
  location = var.region
  # Allows traffic from the public internet to reach the service. Note this
  # controls *network reachability*, not *authorization* — the service still
  # requires a valid OIDC token to invoke (granted to the scheduler SA in iam.tf),
  # so "all ingress" does not mean "anyone can run jobs".
  ingress = "INGRESS_TRAFFIC_ALL"

  template {
    # Run as the dedicated least-privilege runtime SA rather than the default
    # Compute SA, so the container only has the secret/bucket access from iam.tf.
    service_account = google_service_account.runtime.email

    containers {
      image = var.image

      ports {
        # Port the container listens on. Cloud Run also injects $PORT=8080; this
        # must match what the app binds to or health checks fail.
        container_port = 8080
      }

      # Expands local.env into one env{} block per key/value. dynamic avoids
      # hand-writing a block per variable and lets optional features add or omit
      # vars without changing this structure.
      dynamic "env" {
        for_each = local.env
        content {
          name  = env.key
          value = env.value
        }
      }
    }
  }

  # Explicit ordering: don't create the service until its runtime IAM exists.
  # Otherwise the first revision could boot before it has permission to read its
  # secrets / bucket and crash-loop. (Terraform infers the SA dependency from the
  # reference above, but the IAM bindings must be listed explicitly.)
  depends_on = [
    google_project_service.apis,
    google_secret_manager_secret_iam_member.runtime_onedrive_accessor,
    google_secret_manager_secret_iam_member.runtime_telegram_accessor,
    google_storage_bucket_iam_member.runtime_app_bucket,
  ]
}
