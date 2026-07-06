# =============================================================================
# outputs.tf
# -----------------------------------------------------------------------------
# Surfaces values that consumers need after `apply`: printed on the CLI, read by
# CI (e.g. the GitHub Actions auth step needs the WIF provider name and image
# prefix), and available to any downstream tooling that queries the state.
# =============================================================================

output "service_url" {
  description = "Public URL of the Cloud Run service (invocation requires auth)."
  value       = google_cloud_run_v2_service.service.uri
}

output "runtime_service_account" {
  description = "Email of the service account the Cloud Run service runs as."
  value       = google_service_account.runtime.email
}

output "deployer_service_account" {
  description = "Email of the CI deployer service account GitHub Actions impersonates."
  value       = google_service_account.deployer.email
}

# Assembles the full Artifact Registry path from region/project/repo so CI can
# tag and push images to "<prefix>/<image>:<tag>" without reconstructing it.
output "artifact_registry_image_prefix" {
  description = "Base path for container images pushed to Artifact Registry."
  value       = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.containers.repository_id}"
}

output "workload_identity_provider" {
  description = "Full resource name of the WIF provider, for the GitHub Actions auth step."
  value       = google_iam_workload_identity_pool_provider.github.name
}
