# =============================================================================
# wif.tf
# -----------------------------------------------------------------------------
# Sets up Workload Identity Federation so GitHub Actions can deploy without any
# long-lived service-account key. GitHub presents its own OIDC token; GCP trusts
# it (scoped to this repo) and lets the workflow impersonate the deployer SA for
# short-lived credentials. This is the keyless auth path that replaces stored
# JSON keys.
# =============================================================================

# Workload Identity Federation: lets GitHub Actions exchange its OIDC token for
# short-lived credentials to impersonate the deployer SA. No service-account key
# is ever created or stored.
#
# The pool is the trust container; the provider below defines who/what it trusts.
resource "google_iam_workload_identity_pool" "github" {
  workload_identity_pool_id = "github-pool"
  display_name              = "GitHub Actions Pool"

  depends_on = [google_project_service.apis]
}

resource "google_iam_workload_identity_pool_provider" "github" {
  workload_identity_pool_id          = google_iam_workload_identity_pool.github.workload_identity_pool_id
  workload_identity_pool_provider_id = "github-provider"
  display_name                       = "GitHub Actions Provider"

  # Maps claims from GitHub's OIDC token into GCP attributes. repository is used
  # in the condition below and in the impersonation binding's principalSet; ref
  # is mapped so the condition can also pin the branch (see attribute_condition).
  attribute_mapping = {
    "google.subject"       = "assertion.sub"
    "attribute.repository" = "assertion.repository"
    "attribute.ref"        = "assertion.ref"
  }

  # Critical security gate: a token is only accepted if it comes from this exact
  # repo AND from a workflow running on the deploy branch. Repo-only scoping is
  # not enough — the deployer SA this unlocks is near-omnipotent, so without the
  # ref check ANY workflow in the repo (a PR, a pushed feature branch) could mint
  # a token and assume it. deploy.yml only deploys on push to master, so pinning
  # ref to refs/heads/master matches the real deploy path and closes that gap.
  attribute_condition = "assertion.repository == \"${var.github_repository}\" && assertion.ref == \"refs/heads/${var.deploy_branch}\""

  # Tells GCP whose tokens to validate — GitHub Actions' OIDC issuer.
  oidc {
    issuer_uri = "https://token.actions.githubusercontent.com"
  }
}

# Allow workflows running in the configured repository to impersonate the
# deployer SA. The member is a principalSet, not a single principal: it matches
# every federated identity from this pool whose attribute.repository equals the
# configured repo — i.e. any workflow run in that repo, scoped by the mapped
# attribute. This is the binding that actually grants the keyless deploy access.
resource "google_service_account_iam_member" "github_impersonation" {
  service_account_id = google_service_account.deployer.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${google_iam_workload_identity_pool.github.name}/attribute.repository/${var.github_repository}"
}
