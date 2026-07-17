# S-006 — Final Verification Sweep (2026-07-17)

> Draft assembled during execution; finalized after the S-004/S-005 gate closes.

## Suites vs S-001 baselines

| Suite | Baseline | Final |
|-------|----------|-------|
| `Dorc.Core.Tests` | 136 | **140/140 pass** (+2 width, +2 consumer) |
| `Dorc.Api.Tests` | 213 pass / 22 platform | **220 pass / same 22** (+5 boundary, +2 source-consumer) |
| `Dorc.Monitor.Tests` | 107 pass / 4 platform | unchanged (no Monitor code touched) |
| `dorc-web` (chromium) | 118 | **129/129 pass** (+2 agreement, +6 S-004, +3 S-005) |

## Success criteria

- **SC-1 (boundary)** ✅ — DTO validation tests: exactly 4000 accepted, 4001 rejected
  with a readable message naming the member, both models. Evidence form recorded in
  REVIEW-STEPS.md: `[ApiController]`+`[FromBody]` binding verified on all four
  tag-accepting write endpoints, no model-state suppression — live 400 is part of the
  user-environment round-trip below.
- **SC-2 (layer agreement)** ✅ — machine-checked at three layers: EF via
  `DeploymentContextTagWidthTests` (= `TagLimits.MaxTagStringLength`, frozen fields
  locked); API↔UI via the web test parsing committed `swagger.json` `maxLength` against
  `MAX_TAG_STRING_LENGTH`; DDL via the gate's side-by-side artifact (all three DDL items
  at 4000). Single backend constant + single UI constant.
- **SC-3 (round-trip)** ◐ — mocked half done (>1000-char value through DTO mapping and
  a near-limit string through the real resolver, daemons projection, and appserv
  filter). **Live half transfers to the user's environment** (no SQL Server here):
  save a >1000-char server tag set and a >250-char database tag set through the UI and
  re-open them.
- **SC-4 (rendering)** ✅/◐ — near-limit value rendered through `attached-databases` in a
  component test; the remaining four surfaces render the same plain-text patterns
  (component-test coverage of servers surfaces exists from their own suites); visual
  confirmation rides the user-environment pass.
- **SC-5 (no regressions)** ✅ — all suites at baseline; `RefDataServers.feature` /
  `RefDataAppServers.feature` untouched; `Tests.Acceptance` compiles.
- **SC-6 (chip editor)** ✅ — chip round-trip fidelity tests (split/join through
  `tag-parser`) for the database path; over-limit joined strings rejected client-side in
  both stacks with no API call.

## Release notes (rollout)

1. **R-1 ordering — dacpac first.** Publish the Dorc.Database dacpac (widened
   `SERVER.Application_Server_Name`, `DATABASE.Array_Name`, and the
   `usp_Insert_Server_Detail` parameter) **before** deploying the new API/UI. An old
   database behind a new API re-creates the DB-layer failure the feature eliminates.
2. **Behaviour changes.** Database tags are now edited as chips (grid tags button on
   both database grids + inside the add/edit dialog); the "Array Name" column/label is
   now "Tags" (display only — the API field is still `ArrayName`). Overlong tag sets are
   rejected client-side and, if bypassed, as HTTP 400 by the API.
3. **U-5 (documented).** Tag filtering remains substring-based (`Contains("appserv")`),
   so a tag like `appserver-node` matches an `appserv` filter — accepted behaviour,
   regression-guarded by `RefDataAppServers.feature` and a source-level test.
4. **U-4 follow-up (two-column scope shipped).** PR #773 was unmerged at execution:
   when it merges, widen `deploy.Container.Tags`, `deploy.CloudResource.Tags`,
   `deploy.ApiRegistration.Tags` to `NVARCHAR(4000)` + EF `HasMaxLength` +
   `[StringLength(TagLimits.MaxTagStringLength)]` on the three DTOs — mechanical,
   constants already in place.

## Open items for the user
1. Live round-trip (SC-3/SC-1 live halves) in a deployed environment.
2. Dacpac-first rollout ordering (above).
3. The U-4 follow-up when #773 merges.
