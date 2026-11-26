@LiveEnvironment
@EnvironmentCloningAndDatabases
Feature: Environment Cloning, Server and Database Reference Data Lifecycle, and Environment Resources
  In order to provision cloned environments and manage attached servers, databases, cloud resources, containers, and SQL ports
  As a DOrc environment owner or administrator
  I want environment cloning flags, tag-based database lookup and collision detection, server and database CRUD with audit trails, and environment resource endpoints to work end-to-end

  Background:
    Given the live DOrc environment is enabled and the synthetic regression baseline is provisioned

  @LiveEnvironment @EnvironmentCloningAndDatabases
  Scenario: Clone an environment with selective copy flags, attach a tagged database, and detect normalized tag collisions
    Given the live parent environment has properties, project mappings, and tagged databases ready for cloning
    When the user clones the parent environment with CopyPropertyValues and CopyProjectMappings enabled and queries databases by tag
    Then the cloned environment inherits the properties and project mappings while isolating uncopied servers and databases
    And normalized tag lookup returns the attached database and reports a 409 Conflict when normalized tags collide in the same environment scope

  @LiveEnvironment @EnvironmentCloningAndDatabases
  Scenario: Create, attach, detach, audit, and delete servers and databases across reference data endpoints
    Given temporary server and database definitions with tags are prepared for the live child environment
    When the user creates, updates, attaches, queries, detaches, and deletes the server and database and exercises database user membership
    Then GET RefDataServers by environment ID, name, ID, and AppServersByEnvName populate Tags and EnvironmentNames
    And GET RefDataDatabases populates AdGroup and Tags, records ServerAudit and DatabaseAudit entries, and returns 204 NoContent upon deletion

  @LiveEnvironment @EnvironmentCloningAndDatabases
  Scenario: Manage API registrations, Azure cloud resources, containers, audits, and SQL ports
    Given temporary API registration, cloud resource, container, and SQL port payloads are prepared for the child environment
    When the user exercises CRUD and attachment workflows on ApiRegistrations, CloudResources, Containers, Audits, AppServers, and SqlPorts
    Then all reference data items persist, attach to the environment, appear in query responses, and clean up deterministically
