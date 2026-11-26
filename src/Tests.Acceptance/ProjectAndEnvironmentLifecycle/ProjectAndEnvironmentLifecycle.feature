@LiveEnvironment
@ProjectAndEnvironmentLifecycle
Feature: End-to-End Project and Environment Lifecycle from Scratch to Deployment, Secret Redaction, and Audit Trails
  In order to onboard a new application into DOrc and deploy it safely across hierarchical environments
  As a DOrc user
  I want to create parent and child environments, attach servers and databases, configure a project and variables, run a live deployment with secret redaction, and verify all audit trails

  Background:
    Given the live DOrc environment is enabled and the synthetic regression baseline is provisioned

  @LiveEnvironment @ProjectAndEnvironmentLifecycle
  Scenario: Execute a full project and environment lifecycle from the live Web UI through deployment monitoring, secret redaction, and four-way audit verification
    Given brand-new parent and child environments, a tagged server, a tagged database, and a new project with a PowerShell 7 component are provisioned and verified in the live Web UI
    And global, parent-inherited, child-updated, request-overridden, and encrypted secret variables are configured across the hierarchy and verified masked in the live Web UI
    When the user triggers a deployment request from the live Deploy UI on the child environment with a request-level property override and monitors it to completion
    Then the deployment completes in the live Monitor Result UI, the script output confirms hierarchical variable resolution, and cleartext secrets are redacted as REDACTED in all logs
    And the live Audit UI pages and RefDataProjectAudit, PropertyValuesAudit, ServerAudit, and DatabaseAudit record all create and update transitions before deterministic teardown
