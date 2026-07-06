# =============================================================================
# secrets.tf
# -----------------------------------------------------------------------------
# Creates the Secret Manager secret CONTAINERS for the OneDrive and Telegram
# settings. Critically, it creates only the containers, never the secret values
# (versions) — those are seeded out-of-band so sensitive data never lands in
# Terraform state. The runtime SA gets read (and, for OneDrive, write) access in
# iam.tf.
# =============================================================================

locals {
  # Secret names are derived from var.secret_prefix so they match what the app
  # looks up at runtime (COMICDOWNLOADER_SECRET_PREFIX). Change the prefix and
  # both sides move together.
  onedrive_secret_id = "${var.secret_prefix}settings-onedrive"
  telegram_secret_id = "${var.secret_prefix}settings-telegram"
}

# Secret CONTAINERS only. Values (versions) are seeded out-of-band during
# bootstrap and must never be placed in Terraform state. See
# ../bootstrap/README.md for the one-time seeding commands.
#
# The app rotates the OneDrive refresh token at runtime by adding new secret
# versions. Those versions are deliberately not modeled here, so Terraform never
# competes with the running service over them.
resource "google_secret_manager_secret" "onedrive" {
  secret_id = local.onedrive_secret_id

  # auto{} lets Google manage replication across regions automatically (as
  # opposed to a user_managed list of regions). Simplest option and fine unless
  # data-residency rules require pinning specific regions.
  replication {
    auto {}
  }

  depends_on = [google_project_service.apis]
}

resource "google_secret_manager_secret" "telegram" {
  secret_id = local.telegram_secret_id

  replication {
    auto {}
  }

  depends_on = [google_project_service.apis]
}
