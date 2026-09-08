# SPEC S-006 — Typed client regeneration

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | APPROVED (executed under auto-pilot) |
| **IS step**| S-006 (IS v2, APPROVED)              |
| **Date**   | 2026-07-17                           |

## What was found (material discovery, recorded for the gate and S-008)

The committed generated client is **not reproducible from the committed `swagger.json`
with the repo's own toolchain**:

1. `openapitools.json` pinned generator **6.6.0**, but the committed client's
   `.openapi-generator/VERSION` says **7.13.0**. A 6.6.0 run rewrites every file
   (different auth-header emission).
2. Even at 7.13.0, regenerating from the *committed* spec rewrites ~70 files and creates
   files the client folder never had (`AnalyticsApi.ts`, `ConfigValuesApi.ts`,
   `CopyEnvBuildApi.ts`, `ComponentType.ts`, …) — the committed spec and committed client
   have drifted apart independently.
3. A full refresh from the running API **breaks the app build**: main's app code expects
   older generated shapes (e.g. `AccessControlType.NUMBER_0`), so truing-up client ↔ spec
   ↔ API is a repo-wide maintenance task, out of this feature's scope.

## What was done (minimal faithful regeneration)

1. `openapitools.json` pin corrected to **7.13.0** (matches the committed client's
   VERSION marker).
2. Fresh spec obtained from the locally-run API (`/swagger/v1/swagger.json`, per
   TOOLCHAIN-S-001 #5). The 15 new paths (`/RefDataContainers*`, `/RefDataCloudResources*`,
   `/RefDataApiRegistrations*`) and 3 new schemas were **script-spliced verbatim**
   (generator-produced fragments, no hand-authored content) into the committed
   `swagger.json`.
3. The generator ran against the spliced spec into a temp directory; only the six
   generated files for the new types were copied in: `apis/RefDataContainersApi.ts`,
   `apis/RefDataCloudResourcesApi.ts`, `apis/RefDataApiRegistrationsApi.ts`,
   `models/ContainerApiModel.ts`, `models/CloudResourceApiModel.ts`,
   `models/ApiRegistrationApiModel.ts`.
4. `apis/index.ts` / `models/index.ts` gained the six export lines the generator would
   emit for them.

Result: every pre-existing generated file is **byte-identical**; the diff is additions
plus the spec splice and pin fix — the closest achievable reading of SC-5 given the
discovered drift.

## Gates
- **R-3**: additions-only diff verified (`git status` on `apis/dorc-api`: 4 modified —
  spec, two index files, pin — 6 added, 0 deleted).
- **R-5** (name collision): new generated names checked against existing client —
  `RefDataApiRegistrationsApi` vs existing `ApiEndpoints` model: distinct;
  `ContainerApiModel` unique in `models/`; no confusable adjacency found.
- `dorc-web` builds clean against the extended client; web tests at S-001 baseline.

## Follow-up recorded for S-008 / release note
The client/spec/generator drift predates this feature and should be trued up in a
dedicated maintenance change (regenerate everything at a pinned version and fix the app
fallout once).
