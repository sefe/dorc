@LiveEnvironment
@ProjectAndEnvironmentLifecycle
Feature: End-to-End Project and Environment Lifecycle from Scratch to Deployment, Secret Redaction, and Audit Trails
  In order to onboard a new application into DOrc and deploy it safely across hierarchical environments
  As a DOrc user
  I want to create parent and child environments, attach servers and databases, configure a project and variables, run a live deployment with secret redaction, and verify all audit trails

  Background:
    Given the live DOrc environment is enabled and the synthetic regression baseline is provisioned

  @LiveEnvironment @ProjectAndEnvironmentLifecycle
  Scenario: Execute a full project and environment lifecycle from scratch through deployment monitoring, secret redaction, and four-way audit verification
    Given brand-new parent and child environments, a tagged server, a tagged database, and a new project with a PowerShell 7 component are provisioned
    And global, parent-inherited, child-updated, request-overridden, and encrypted secret variables are configured across the hierarchy
    When the user triggers a deployment request on the child environment with a request-level property override and waits for completion
    Then the deployment completes, the script output confirms hierarchical variable resolution, and cleartext secrets are redacted as REDACTED in all logs
    And RefDataProjectAudit, PropertyValuesAudit, ServerAudit, and DatabaseAudit record all create and update transitions before deterministic teardown
