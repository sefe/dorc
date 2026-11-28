@LiveEnvironment
@PropertyValuesAndConfiguration
Feature: Multi-Environment Property Lifecycle, Scoped Search, Audit, Export, and System Configuration Metadata
  In order to manage deployment variables across environments and configure system-wide DOrc settings
  As a DOrc user or administrator
  I want property definition CRUD, scoped property values, audit history, property export, RefDataConfig, and runtime metadata endpoints to work end-to-end

  Background:
    Given the live DOrc environment is enabled and the synthetic regression baseline is provisioned

  @LiveEnvironment @PropertyValuesAndConfiguration
  Scenario: Create, update, search, audit, export, and delete properties and environment-scoped values
    Given temporary plain, secure, and array property definitions are prepared for multi-environment lifecycle testing
    When the user creates the properties, assigns default and environment-scoped values, searches RefDataScopedPropertyValues, inspects PropertyValuesAudit, and exports the properties
    Then duplicate property values are rejected, secure values are masked in scoped queries, and audit records capture Insert, Update, and Delete transitions
    And Export returns the expected property definitions and values before all temporary properties are deleted cleanly

  @LiveEnvironment @PropertyValuesAndConfiguration
  Scenario: Manage RefDataConfig settings and query PowerShell versions, API configuration, and runtime metadata
    Given a temporary configuration key-value pair is prepared for RefDataConfig lifecycle testing
    When the user creates, updates, queries, and deletes the configuration key and requests PowerShellVersions, ApiConfig, and Metadata endpoints
    Then RefDataConfig persists and deletes the configuration entry and all runtime metadata endpoints return valid responses
