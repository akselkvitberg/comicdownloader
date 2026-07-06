# =============================================================================
# iam.tf
# -----------------------------------------------------------------------------
# Grants permissions to the three service accounts from service_accounts.tf.
# This is where least-privilege is actually enforced: the runtime SA gets only
# what the app needs at request time, the scheduler SA gets only the right to
# invoke the service, and the deployer SA gets the broader admin roles CI needs.
# Bindings here use the *_iam_member resources, which add a single member to a
# role without taking over (authoritatively replacing) the whole policy.
# =============================================================================

###############################################################################
# Runtime service account permissions
###############################################################################

# Read both secrets.
resource "google_secret_manager_secret_iam_member" "runtime_onedrive_accessor" {
  secret_id = google_secret_manager_secret.onedrive.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.runtime.email}"
}

resource "google_secret_manager_secret_iam_member" "runtime_telegram_accessor" {
  secret_id = google_secret_manager_secret.telegram.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.runtime.email}"
}

# Write new versions of the OneDrive secret. This is the permission the old
# deploy script was missing, which caused the runtime PermissionDenied on
# secretmanager.versions.add during the refresh-onedrive job.
resource "google_secret_manager_secret_iam_member" "runtime_onedrive_version_adder" {
  secret_id = google_secret_manager_secret.onedrive.id
  role      = "roles/secretmanager.secretVersionAdder"
  member    = "serviceAccount:${google_service_account.runtime.email}"
}

# Read/write the application bucket (images + dedupe markers). objectAdmin (not
# the broader storage.admin) lets the app create/read/delete objects but not
# reconfigure or delete the bucket itself — scoped to this one bucket.
resource "google_storage_bucket_iam_member" "runtime_app_bucket" {
  bucket = google_storage_bucket.app.name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.runtime.email}"
}

# Read the optional Calvin and Hobbes source bucket. objectViewer = read-only;
# the app only needs to pull source scans, never write to this bucket.
# Counted to match the optional bucket: created only when the feature is on, and
# it references the bucket via [0] because that resource is itself counted.
resource "google_storage_bucket_iam_member" "runtime_calvin_source" {
  count = var.calvin_hobbes_source_bucket == "" ? 0 : 1

  bucket = google_storage_bucket.calvin_hobbes_source[0].name
  role   = "roles/storage.objectViewer"
  member = "serviceAccount:${google_service_account.runtime.email}"
}

###############################################################################
# Scheduler service account permissions
###############################################################################

# Allow Cloud Scheduler's identity to invoke the authenticated service. The
# Cloud Run service requires auth (it is not public), so the scheduler SA needs
# run.invoker; without it the scheduled HTTP calls would get 403. scheduler.tf
# depends_on this binding so the jobs aren't created before they can authenticate.
resource "google_cloud_run_v2_service_iam_member" "scheduler_invoker" {
  location = google_cloud_run_v2_service.service.location
  name     = google_cloud_run_v2_service.service.name
  role     = "roles/run.invoker"
  member   = "serviceAccount:${google_service_account.scheduler.email}"
}

###############################################################################
# CI deployer service account permissions
###############################################################################

# Project-level admin roles CI needs to apply this whole configuration. These
# are intentionally broad because Terraform must create/update every kind of
# resource in this repo. Each role lines up with a file: run.admin (cloud_run),
# artifactregistry.writer (image push), cloudscheduler.admin (scheduler),
# secretmanager.admin (secrets), storage.admin (buckets), the iam/resourcemanager
# roles (SAs, WIF, IAM bindings), and serviceusage (enabling APIs).
locals {
  deployer_project_roles = [
    "roles/run.admin",
    "roles/artifactregistry.writer",
    "roles/cloudscheduler.admin",
    "roles/secretmanager.admin",
    "roles/storage.admin",
    "roles/iam.serviceAccountAdmin",
    "roles/iam.workloadIdentityPoolAdmin",
    "roles/resourcemanager.projectIamAdmin",
    "roles/serviceusage.serviceUsageAdmin",
    # Required to deploy Cloud Run / Scheduler that run AS the other SAs.
    # Without serviceAccountUser, the deployer can't "act as" the runtime/
    # scheduler SAs, and creating those resources fails with a permission error.
    "roles/iam.serviceAccountUser",
  ]
}

# Binds every role above to the deployer SA at the project level (one binding
# per role via for_each).
resource "google_project_iam_member" "deployer" {
  for_each = toset(local.deployer_project_roles)

  project = var.project_id
  role    = each.value
  member  = "serviceAccount:${google_service_account.deployer.email}"
}
