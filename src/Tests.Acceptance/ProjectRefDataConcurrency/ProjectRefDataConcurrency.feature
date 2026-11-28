@LiveEnvironment
@ProjectRefDataConcurrency
Feature: Project RefData Optimistic Concurrency, Shared Script Boundaries, and Environment Mapping Lifecycle
  In order to prevent lost updates when editing project definitions and manage environment mappings safely
  As a DOrc user or project owner
  I want optimistic concurrency revision checks on RefData, shared script protection, and project-to-environment mapping workflows to work end-to-end

  Background:
    Given the live DOrc environment is enabled and the synthetic regression baseline is provisioned

  @LiveEnvironment @ProjectRefDataConcurrency
  Scenario: Enforce optimistic concurrency revision tokens on PUT RefData and preserve shared scripts across projects
    Given the synthetic project has a RefData definition with a current Revision concurrency token
    When the user submits PUT RefData with a missing revision token, a stale revision token, and a valid revision token with a local script edit
    Then missing revision tokens return 428 Precondition Required and stale revision tokens return 409 Conflict without partial writes
    And saving with the current revision token succeeds, advances the revision token, and preserves shared script boundaries

  @LiveEnvironment @ProjectRefDataConcurrency
  Scenario: Map and unmap environments to a project and verify project lookup and environment hierarchy chains
    Given a temporary environment and project mapping state are prepared for project and environment hierarchy verification
    When the user queries RefDataProjects, links and unlinks parent-child environments, and maps and unmaps the environment on the project
    Then project lookup, environment parent chain validation, and project-environment mapping creation and deletion succeed cleanly
