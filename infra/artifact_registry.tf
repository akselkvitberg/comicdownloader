# =============================================================================
# artifact_registry.tf
# -----------------------------------------------------------------------------
# Creates the Artifact Registry Docker repository that holds the application's
# container images. CI pushes built images here; Cloud Run pulls the deployed
# image from here (see var.image / outputs.tf image prefix).
# =============================================================================

resource "google_artifact_registry_repository" "containers" {
  location = var.region
  # ID only (not the full path). The full pullable path is
  # <region>-docker.pkg.dev/<project>/<repository_id> — assembled in outputs.tf.
  repository_id = var.artifact_registry_repository
  # Repository flavour. DOCKER stores OCI/Docker images; other formats (NPM,
  # MAVEN, etc.) exist but would reject container pushes.
  format      = "DOCKER"
  description = "Comic Downloader container images"

  # Wait until the artifactregistry API is enabled before creating the repo.
  depends_on = [google_project_service.apis]
}
