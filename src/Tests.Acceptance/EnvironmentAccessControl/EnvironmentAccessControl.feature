@LiveEnvironment
@EnvironmentAccessControl
Feature: Environment Access Control, Active Directory Search, and Database User Permissions
  In order to govern who can administer environments and access attached databases
  As a DOrc administrator or environment owner
  I want Active Directory search, environment ACL owner protection, privilege grant and revoke, and database user permissions to work end-to-end

  Background:
    Given the live DOrc environment is enabled and the synthetic regression baseline is provisioned

  @LiveEnvironment @EnvironmentAccessControl
  Scenario: Search Active Directory users and groups and prevent removing the last environment owner
    Given the live parent environment has an access control list with an assigned owner
    When the user searches Active Directory users and groups and attempts to strip all Owner privileges from the environment ACL
    Then DirectorySearch returns matching users and groups with 200 OK
    And PUT AccessControl rejects removing the last environment owner with 400 BadRequest

  @LiveEnvironment @EnvironmentAccessControl
  Scenario: Search directory principals, grant and revoke environment ACL privileges, and manage database user permissions
    Given registered users, roles, groups, and a temporary environment are available for access control testing
    When the user validates account existence, grants and revokes environment ACL privileges, and assigns and deletes custom database permissions
    Then Account existence checks validate Windows users and groups and reject invalid account types with 400 BadRequest
    And environment ACL privileges and custom database permissions are persisted, verified, and revoked cleanly
