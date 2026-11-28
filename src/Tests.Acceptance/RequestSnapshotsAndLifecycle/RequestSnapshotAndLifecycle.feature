@LiveEnvironment
@RequestSnapshotsAndLifecycle
Feature: Deployment Request Snapshot Immutability, Component Restart, and In-Flight Cancellation
  In order to retry failed components predictably, cancel stuck deployments, and guarantee queued requests run their submitted snapshot
  As a DOrc user or release operator
  I want component restarts after variable fixes, in-flight request cancellation, and immutable request snapshots to work end-to-end

  Background:
    Given the live DOrc environment is enabled and the synthetic regression baseline is provisioned

  @LiveEnvironment @RequestSnapshotsAndLifecycle
  Scenario: Restart a failed deployment request after updating the controlling property and complete on the second attempt
    Given the gate property is set to fail so that the initial deployment request fails
    When the user submits the deployment request, waits for Failed status, updates the gate property to pass, and restarts the request
    Then the restarted deployment request executes a second attempt and transitions to Completed
    And the prior failed attempt history is preserved alongside the completed retry

  @LiveEnvironment @RequestSnapshotsAndLifecycle
  Scenario: Cancel an in-flight deployment request and terminate the running PowerShell execution
    Given a long-running deployment request is submitted on the live child environment
    When the user cancels the deployment request via PUT Request cancel while it is Pending or Running
    Then the deployment request transitions to Cancelled and stops executing

  @LiveEnvironment @RequestSnapshotsAndLifecycle
  Scenario: Replay the original submitted script snapshot when restarting a request after a project script definition edit
    Given a deployment request is submitted and completed using version 1 of a project component script
    When the project owner edits the component script definition to version 2 and restarts the original deployment request while also submitting a new request
    Then the restarted original request replays the immutable version 1 snapshot from when it was submitted
    And the newly submitted deployment request executes the updated version 2 script definition
