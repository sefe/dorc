@LiveEnvironment
@BundledRequestsMakeLikeProd
Feature: Bundled Requests and Make-Like-Prod Releases
  In order to refresh non-production environments and deploy multi-component release bundles reliably
  As a DOrc release engineer or environment owner
  I want Make-Like-Prod job bundles, bundle.json manifest validation, ReleaseInformation, and CopyEnvBuild to execute end-to-end

  Background:
    Given the live DOrc environment is enabled and the synthetic regression baseline is provisioned

  @LiveEnvironment @BundledRequestsMakeLikeProd
  Scenario: Dispatch a configured Make-Like-Prod bundled job request and resolve AllRequestIds
    Given a Make-Like-Prod bundled job request is configured for the synthetic project with AllRequestIds interpolation
    When the user triggers Make-Like-Prod against the live child environment using that bundle
    Then DOrc dispatches the bundled deployment request to the child environment and executes it to Completed
    And the runner execution log confirms that AllRequestIds was populated and the temporary bundle definition is cleaned up

  @LiveEnvironment @BundledRequestsMakeLikeProd
  Scenario: Validate bundle.json manifests, expand multi-build bundle requests, and copy environment builds
    Given valid and invalid bundle.json manifests are staged in the local bundle artefact drop folder
    When the user queries BundleComponents, submits a multi-component bundle deployment, requests ReleaseInformation, and triggers CopyEnvBuild
    Then BundleComponents returns distinct components for a valid manifest and rejects unknown components with 400 BadRequest
    And the bundle deployment expands per component build number, completes, returns release information, and copies the build to the child environment
