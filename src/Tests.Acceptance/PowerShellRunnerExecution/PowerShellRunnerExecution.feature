@LiveEnvironment
@PowerShellRunnerExecution
Feature: PowerShell Runner Execution Across .NET 8, .NET Framework 4.8, StopOnFailure, Installed CLI Tools, and IsArray Variables
  In order to execute automated deployment scripts reliably across PowerShell 7 and Windows PowerShell 5.1
  As a DOrc user or release engineer
  I want hierarchical variable resolution, secret redaction, multi-component StopOnFailure gating, ADO and GitHub artefact resolution, installed CLI tools, and IsArray string[] variable injection to work end-to-end

  Background:
    Given the live DOrc environment is enabled and the synthetic regression baseline is provisioned

  @LiveEnvironment @PowerShellRunnerExecution
  Scenario: Execute a PowerShell 7 deployment with updated property values, hierarchy inheritance, request overrides, and secret redaction
    Given hierarchical properties, a request override property, and an encrypted secret property are configured for the synthetic project
    When the user submits a PowerShell 7 deployment request on the live child environment with a request property override
    Then the runner executes on PowerShell Core, resolves global, parent-inherited, child-updated, and request-overridden values, and completes
    And the secret value is verified inside the script while being redacted as REDACTED in all deployment logs

  @LiveEnvironment @PowerShellRunnerExecution
  Scenario: Execute a .NET Framework 4.8 Windows PowerShell 5.1 deployment and inject resolved properties
    Given the synthetic project includes a component configured for Windows PowerShell 5.1 on the .NET Framework 4.8 runner
    When the user submits a deployment request for the .NET Framework 4.8 runner component on the live child environment
    Then the deployment executes under Windows PowerShell Desktop edition and completes with all resolved properties injected

  @LiveEnvironment @PowerShellRunnerExecution
  Scenario: Halt downstream components when an upstream component fails with StopOnFailure enabled
    Given a two-component deployment sequence where the first component fails and has StopOnFailure enabled
    When the user submits a deployment request for both components on the live child environment
    Then the overall deployment request transitions to Failed
    And the first component is marked Failed while the downstream component is never executed

  @LiveEnvironment @PowerShellRunnerExecution
  Scenario: Resolve Azure DevOps and GitHub artefact builds and execute installed DOrc CLI tools on the target host
    Given the synthetic project is configured to query build servers and installed DOrc CLI binaries on the deployment host
    When the user queries BuildServers and ProjectBuilds endpoints and executes the installed CLI tools with OAuth client credentials
    Then the build server and artifact resolution endpoints respond and the installed CLI tools authenticate and execute without missing Scope errors

  @LiveEnvironment @PowerShellRunnerExecution
  Scenario: Inject IsArray string array variables across plain, interpolated, nested, secure, and request-overridden scopes into both runners
    Given IsArray properties for plain, interpolated, nested, encrypted secure, and request-overridden string arrays are configured via the API
    When the user updates the array properties, verifies RefDataScopedPropertyValues and Export, and runs PowerShell 7 and Windows PowerShell 5.1 deployments
    Then both runners receive genuine PowerShell string arrays with exact element counts, interpolated elements, and request overrides
    And secure array element values are verified in-script and redacted as REDACTED in all runner and deployment logs
