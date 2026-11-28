@LiveEnvironment
@TerraformCatalogue
Feature: Terraform Stock Module Catalogue Discovery, Validation, and Optimistic Concurrency
  In order to configure and govern infrastructure components from curated Terraform stock modules
  As a DOrc user or platform engineer
  I want template discovery, component instantiation, parameter resolution, regex input validation, and revision concurrency checks to work end-to-end

  Background:
    Given the live DOrc environment is enabled and the synthetic regression baseline is provisioned

  @LiveEnvironment @TerraformCatalogue
  Scenario: Discover stock Terraform templates, instantiate a component, validate manifest inputs, and enforce optimistic concurrency
    Given the stock Terraform module catalogue contains the service-bus and kafka-topic templates
    When the user instantiates a service-bus 1.0.0 component on the synthetic project and tests input validation, revision concurrency, and upgrade guards
    Then template resolution returns the project and environment parameters
    And invalid input patterns and unconfirmed or same-version upgrades return 400 BadRequest while stale configuration revisions return 409 Conflict
