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

## Audit pages for the new component types
Each of the three new entity types gets a read path over its audit trail, mirroring the
daemons audit feature:
- API: `PUT /ContainerAudit`, `PUT /CloudResourceAudit`, `PUT /ApiRegistrationAudit`
  (paged, filterable by user/action, optionally restricted to one record via
  `containerId`/`cloudResourceId`/`apiRegistrationId`).
- UI: **Containers Audit**, **Cloud Resources Audit** and **APIs Audit** pages under the
  navbar's Audit submenu (`/containers/audit`, `/cloud-resources/audit`,
  `/api-registrations/audit`), with create/delete row colouring and per-character
  from/to value diffs. Each row on the environment Containers/Cloud/APIs tabs has an
  Audit button deep-linking to that record's history. Audit rows for deleted components
  are retained and render as *(deleted)*.

## Behaviour changes
- Shared definition writes now authorize the complete current membership and save
  their audit in one serializable SQL transaction, retried as a unit. The database's
  membership range locks also block concurrent clone/registrar/direct-SQL attachment
  inserts until the write completes; membership cannot change between authorization
  and mutation. Audit failures roll back create/update/delete/attach/detach.
- Full-definition `PUT` requires the `Revision` returned by the last read; stale
  definitions return **409** rather than overwrite concurrent edits. Tag editing uses
  `PUT /RefData{Containers|CloudResources|ApiRegistrations}/{id}/tags` with only
  `ExpectedTags` and `Tags`, preserving unrelated fields and rejecting stale tags.
- All three grids distinguish PowerUser/Admin creation, local environment
  attach/detach, and global definition edit/tags/delete permissions. Capabilities are
  advisory UI hints; the write transaction always re-authorizes current membership.
  Global edits and deletion explicitly confirm all affected environments. Definition
  deletion removes DOrc metadata and attachments only, never deployed infrastructure.
- A server with a **null** tag string no longer crashes variable resolution (previously
  an unhandled NullReferenceException aborted the deployment); it now simply yields no
  per-tag variables.
- Environment deletion now detaches any attached containers/cloud resources/API
  registrations, mirroring servers/databases handling.
- Attach/detach endpoints now return **404** (previously 500) when the target
  environment id does not exist.

## Known follow-ups (out of this change's scope)
- The complete DOrc web client is now regenerated from committed Swagger using the
  pinned generator; a second generation is byte-identical. Historical initial-client
  drift is described in `SPEC-S-006-client-regeneration.md`.
- **Tag capacity expansion** (servers/databases, >250 chars) ships separately; carry-over
  findings live in `REVIEW-HLPS-round1.md`.
