# =============================================================================
# storage.tf
# -----------------------------------------------------------------------------
# Creates the GCS buckets the application uses: a required app bucket for
# downloaded comic images and dedupe markers, and an optional read-only source
# bucket for Calvin and Hobbes scans. IAM access to these is granted in iam.tf.
# =============================================================================

# Primary application bucket: stores downloaded images and the dedupe markers
# that stop the same comic being re-downloaded.
resource "google_storage_bucket" "app" {
  name     = var.app_bucket_name
  location = var.region
  # Disables legacy per-object ACLs and enforces IAM-only access. Required for
  # the bucket-level IAM bindings in iam.tf to be the single source of truth,
  # and is Google's recommended security posture.
  uniform_bucket_level_access = true
  # Safety guard: with force_destroy = false, `terraform destroy` refuses to
  # delete the bucket while it still contains objects, preventing accidental
  # loss of downloaded comics.
  force_destroy = false

  depends_on = [google_project_service.apis]
}

# Optional private source bucket for Calvin and Hobbes scans. Created only when
# a name is supplied.
resource "google_storage_bucket" "calvin_hobbes_source" {
  # count = 0 means the resource is not created at all. The ternary turns the
  # empty-string sentinel from var.calvin_hobbes_source_bucket into a 0/1 toggle.
  # Because this is a counted resource, it is referenced elsewhere with [0].
  count = var.calvin_hobbes_source_bucket == "" ? 0 : 1

  name                        = var.calvin_hobbes_source_bucket
  location                    = var.region
  uniform_bucket_level_access = true
  force_destroy               = false

  depends_on = [google_project_service.apis]
}
