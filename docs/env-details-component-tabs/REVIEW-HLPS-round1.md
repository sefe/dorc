# Adversarial Review — HLPS env-details-component-tabs — Round 1

Panel: 3 independent reviewers (completeness lens, architecture/risk lens, process/decision-quality lens).
Verdict: REVISION required. 2 HIGH, 10 MEDIUM, 11 LOW. Triage below; all revisions applied in HLPS v2.

> **Scope change after round 1 (user direction, 2026-07-16):** tag capacity expansion moves to a
> **separate PR** with its own HLPS. Tag-specific findings below (B1, B3, B4, B7, B8, C5, C11, A6 and
> the tag parts of A3/A4) are triaged **Accept — carried over** to that future document rather than
> into HLPS v2, which now covers the component tabs only.

| ID | Sev | Finding (abridged) | Triage | Resolution in HLPS v2 |
|----|-----|--------------------|--------|------------------------|
| B1 | HIGH | `usp_Insert_Server_Detail.sql` param `@APPLICATION_SERVER_NAME NVARCHAR(1000)` keeps silent truncation after column widened | Accept | Added to §5.2 change list |
| C1 | HIGH | Mirroring `RefDataServersController` auth verbatim leaves `Post` ungated, unattached items writable by anyone, `Delete` returns 200+false not 403 — contradicts §6 | Accept | §5.1.4 rewritten: gate per `RefDataDaemonsController` precedent (PowerUser/Admin for create & unattached writes; `CanModifyEnvironment` over mapped envs otherwise; 403 responses). U-4 updated |
| C2 | MED | Attach/detach actually lives in `ApiServices.ChangeEnvComponent` stringly dispatcher, not `ServersPersistentSource` | Accept | §5.1 corrected; new U-9: typed attach/detach endpoints (recommended) vs extending dispatcher |
| C3 | MED | Mirroring `ServersPersistentSource` copies 3 concrete defects (null-guard, missing Include, unloaded `Environments` → `UserEditable` wrongly true) | Accept | §5.1 gains explicit do-not-copy list |
| C4 | MED | R-1 mitigation contradicts monolithic `GetEnvironmentsDetails`; extending `EnvironmentContentApiModel` bloats every env load | Accept (design change) | §5.1.5 replaced: per-type `GetByEnvId` endpoints, tabs self-fetch (env-daemons precedent); `EnvironmentContentApiModel` untouched; R-1 removed |
| C5 | MED | NVARCHAR(4000) "preserves indexing" rationale false (>1700-byte key limit; `LIKE '%…%'` can't seek) | Accept | §5.2/§7 rationale corrected: avoids LOB/MAX semantics only |
| C6 | MED | `deploy.EnvironmentServer`/`EnvironmentDatabase` SSDT tables have no PK/unique — "mirror the style" copies missing integrity | Accept | §5.1.1 mandates composite PK on new join tables |
| C7 | MED | No risk covers SSDT/EF dual schema source (`EnsureCreated`) or deploy ordering (dacpac before API) | Accept | New R-4 + §4 constraint |
| B2 | MED | DI registration (`PersistentDataRegistry.cs`, Monitor registry) absent from scope | Accept | Added to §3/§5.1 |
| B3 | MED | `add-edit-database.ts` shares one `maxFieldLength=50` across 4 fields; naive raise loosens name/type/instance | Accept | §5.2.3 requires per-field limits |
| B4 | MED | R-2 "no in-repo consumers found beyond those listed" false — `GetAppServerDetails`, `DaemonsPersistentSource`, `RefreshEndur`, `VariableScopeOptionsResolver` | Accept | R-2 rewritten with full consumer list (all width-compatible) |
| A1/A2/B6/C8 | MED/LOW | U-5 already answered by `dorc-web/README.md:213` (swagger from running API); hand-edit fallback contradicts §4 constraint | Accept (merge) | U-5 resolved: regen from locally run API; hand-edit fallback removed; escalates at IS if API can't run in dev env |
| A3 | MED | Success criteria hardcode 4000 while U-6 open; boundary untested | Accept | §6 parameterized on limit N; boundary N accepted / N+1 rejected |
| A4 | MED | Round-trip criterion has no in-scope test vehicle | Accept | §6 names vehicle: API-level boundary tests in `Dorc.Api.Tests` + web component tests + manual UI round-trip at review |
| A5 | MED | §3 pre-decides blocking U-3 | Accept | §3 reworded as recommended-pending-U-3 |
| B5 | LOW | Test scope dropped `Dorc.Core.Tests`, ignores `Tests.Acceptance` (`RefDataServers.feature` exercises ApplicationTags) | Accept | §3 test scope expanded |
| B7 | LOW | 400-production mechanism unstated | Accept | §5.2.2: `[StringLength]` DataAnnotations + `[ApiController]` auto-400 |
| B8 | LOW | Long-value rendering unverified for DB grids (`attached-databases.ts`, `page-databases-list.ts`) | Accept | Added to §5.2.3 |
| C9 | LOW | `ApiEndpoint` collides with existing `ApiEndpoints` service class; "Component" domain term overloaded | Accept | Working name changed to `ApiRegistration`; noted in U-1 |
| C10 | LOW | New tables proposed in legacy `dbo`; newer reference entities live in `deploy` | Accept | §5.1.1 recommends `deploy` schema |
| C11 | LOW | Longer tag strings amplify substring-match false positives (`Contains("appserv")`, `ContainsExpression`) | Accept | Noted under R-2 |
| A6 | LOW | U-7 over-classified blocking | Accept — carried over | U-7 is tag work; finding carries to the separate tag PR's HLPS with the other tag findings |
| A7 | LOW | §5.1.4 commits to audit while U-8 open | Accept | Cross-reference added |
| A8 | LOW | R-1 mitigation unfalsifiable | Superseded | R-1 removed by C4 design change |
| A9 | LOW | Unknowns Register lacks Owner column (exemplar has one) | Accept | Column added |

Rejected: none. Deferred: none.
