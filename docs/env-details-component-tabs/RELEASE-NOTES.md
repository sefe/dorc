# Release Notes — Environment Details Component Tabs

## What ships
The environment details **Components** tab's three placeholder sub-tabs are now real:
**Containers**, **Cloud**, and **APIs**. Each supports create/edit/delete of reference
items, attach/detach to environments (many-to-many), full audit rows on every write,
and — per the U-3 decision — deploy-time exposure of attached items as deployment
variables.

## Deploy ordering requirement (R-4 — read before deploying)
Schema first, always: publish the **Dorc.Database dacpac before** deploying the new API
and Monitor. Nine new tables land in the `deploy` schema (`Container`, `CloudResource`,
`ApiRegistration`, their `Environment*` join tables with composite PKs, and their
`*Audit` tables). The Monitor's variable resolver now depends on the three component
sources at startup; running a new Monitor against an old schema fails at first
environment-details query, not at boot.

## New reserved deployment variable names (R-7)
The following variable names are now emitted by the resolver **when an environment has
attached components** and override any user-defined property of the same name during
resolution (same precedence as the existing `AllServers`/`ServerNames_*`):
`EnvironmentContainers`, `EnvironmentCloudResources`, `EnvironmentApiRegistrations`,
`ContainerNames_<tag>`, `CloudResourceNames_<tag>`, `ApiRegistrationNames_<tag>`.
**Ops action before rollout:** check the Properties data for collisions with these names
and prefixes (the dev environment used for this change has no database access —
TOOLCHAIN-S-001 #8).

Environments with no attached components produce **exactly** the pre-change variable set
(characterization-tested), so existing deployments are unaffected until someone attaches
a component.

## Behaviour changes
- A server with a **null** tag string no longer crashes variable resolution (previously
  an unhandled NullReferenceException aborted the deployment); it now simply yields no
  per-tag variables.
- Environment deletion now detaches any attached containers/cloud resources/API
  registrations, mirroring servers/databases handling.

## Known follow-ups (out of this change's scope)
- **Generated web client drift**: the committed `dorc-api` client, committed
  `swagger.json`, and the running API had drifted apart independently (generator pin said
  6.6.0, client was built with 7.13.0, spec lagged several controllers). This change
  added its six client files via a minimal faithful splice and corrected the pin; a
  dedicated maintenance change should regenerate everything and absorb the app fallout.
  Details: `SPEC-S-006-client-regeneration.md`.
- **Tag capacity expansion** (servers/databases, >250 chars) ships separately; carry-over
  findings live in `REVIEW-HLPS-round1.md`.
- Deferred concurrency edges: concurrent double-detach and duplicate-name unique-index
  races can still surface as 500s (recorded in `REVIEW-STEPS.md`, S-004).
