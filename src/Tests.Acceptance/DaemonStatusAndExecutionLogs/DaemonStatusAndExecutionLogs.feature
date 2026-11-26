@LiveEnvironment
@DaemonStatusAndExecutionLogs
Feature: Windows Service Daemon Status Probing, Lifecycle Management, Analytics, and SignalR
  In order to monitor and control Windows service daemons on environment servers and inspect deployment analytics
  As a DOrc user or operations engineer
  I want to probe live Windows services, manage daemon reference data and server attachments, stop and start services, and query analytics and SignalR endpoints

  Background:
    Given the live DOrc environment is enabled and the synthetic regression baseline is provisioned

  @LiveEnvironment @DaemonStatusAndExecutionLogs
  Scenario: Probe live Windows service daemon status and environment component statuses on an attached server
    Given the target DOrc application server is attached to the live child environment
    When the user queries DaemonStatus, GetComponentStatues, and PropertyValuesAudit for the child environment
    Then the live daemon probe reports running Windows services including W3SVC
    And the environment component status and property audit endpoints return 200 OK before detaching the server

  @LiveEnvironment @DaemonStatusAndExecutionLogs
  Scenario: Manage daemon reference data, server attachments, audit trails, service stop and start, analytics, and SignalR negotiation
    Given a temporary daemon definition and server are prepared for daemon lifecycle verification
    When the user creates, updates, attaches, detaches, audits, and deletes the daemon and stops and restarts NetTcpActivator on the live server
    Then duplicate daemon creation returns 409 Conflict, mismatched IDs return 400 BadRequest, and unknown daemons return 404 NotFound
    And NetTcpActivator transitions cleanly between Stopped and Running with recorded daemon observations, all 12 Analytics endpoints respond, and SignalR negotiation succeeds
