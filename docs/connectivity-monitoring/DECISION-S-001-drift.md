# DECISION-S-001 — PR #374 drift assessment & route choice

| Field | Value |
|---|---|
| Status | IN REVIEW |
| Owner | Ben Hegarty |
| Governing IS | `docs/connectivity-monitoring/IS-connectivity-monitoring.md` (APPROVED) |
| Governing SPEC | `docs/connectivity-monitoring/SPEC-S-001-drift-assessment.md` (APPROVED) |
| Step | S-001 |
| `pull/374/head` SHA at investigation | `6fa37c192fd18cf452288c72e37a03112ecc37b1` |
| `origin/main` SHA at investigation | `f70404e66db5da7cf65feead4e3181c4fbbf9eb9` |
| Merge-base SHA | `d17750039a81d0cced5cf1a4fa7f0a5f05acc431` |
| Post-merge named ref | `origin/copilot/create-server-db-existence-check` (PR #374's source branch on `sefe/dorc`) |
| Post-merge tip SHA | `7bfee34e1b4273a9455e1711e3a60ac2358cf0de` (pushed 2026-04-27; fast-forward, no force-push) |
| Investigation date | 2026-04-27 |

---

## 1. Pre-execution self-audit

| Check | Status | Evidence |
|---|---|---|
| HLPS status APPROVED + user-approved | PASS | `docs/connectivity-monitoring/HLPS-connectivity-monitoring.md` front-matter; user "approved" 2026-04-27 |
| IS status APPROVED + user-approved | PASS | `docs/connectivity-monitoring/IS-connectivity-monitoring.md` front-matter; user "approved" 2026-04-27 |
| SPEC-S-001 status APPROVED + user-approved | PASS | `docs/connectivity-monitoring/SPEC-S-001-drift-assessment.md` front-matter; user "approved" 2026-04-27 |
| No in-flight adversarial reviews | PASS | No outstanding reviewer tasks against connectivity-monitoring artifacts |
| Working tree clean | PASS | `git status` shows no tracked changes; only untracked working dirs (.claude/, docs/connectivity-monitoring/, etc.) |
| Network access to GitHub | PASS | `gh api` and `git fetch` both succeed against `sefe/dorc` |
| **CI baseline on `origin/main`** | **PASS — ALL GREEN** | `gh api repos/sefe/dorc/commits/f70404e66db5da7cf65feead4e3181c4fbbf9eb9/check-runs` returned 8 check-runs all `conclusion: success`: Test Results, Dependabot, Analyze (actions), Analyze (javascript-typescript) ×2, Analyze (csharp) ×2, build. |

A4 acceptance bar therefore stands at full strength: **all required GitHub checks must be green at the post-merge tip** (no baseline-failure carve-out needed).

## 2. Decision

**Chosen route: REVISE.**

Post-merge branch tip SHA: `7bfee34e1b4273a9455e1711e3a60ac2358cf0de` (`origin/copilot/create-server-db-existence-check`).

**Rationale (one paragraph):** PR #374's `ConnectivityCheckService.cs` is a `BackgroundService` (commit `48319b8b` "Changed ConnectivityCheckService to implement BackgroundService and added Yield to MonitorService"). HLPS U-2 — resolved 2026-04-27 — mandates an `IHostedService` + `System.Threading.Timer` impl, not `BackgroundService`. That is a behavioural shape change that no merge resolution can produce; it requires substituting the host abstraction. Independently, U-1 — also resolved 2026-04-27 — mandates a TCP/445 (SMB) probe as a fallback when ICMP ping fails, while PR #374's `ConnectivityChecker.CheckServerConnectivityAsync` is ICMP-only with no fallback (`src/Dorc.Core/Connectivity/ConnectivityChecker.cs:17-34` at `6fa37c19`). Either of these alone forces revise; both together remove all doubt. The merge of `origin/main` into PR #374 is the prerequisite for either route, so it is mechanically identical to ratify; the divergence is in what S-002+ then do on top of that merge.

Force-push is **NOT** required for the merge — PR #374's branch is `copilot/create-server-db-existence-check` on `sefe/dorc` (same repo, head OID `6fa37c19...`); merging `origin/main` produces a merge commit that fast-forwards the remote branch. No history rewrite, no force-push, so the §3 force-push consent gate does not apply for this S-001 setup.

## 3. Investigation findings

### Q1 — Commit graph and drift size

**Evidence (concrete git output):**
```
$ git rev-parse pr-374 origin/main
6fa37c192fd18cf452288c72e37a03112ecc37b1   # pr-374
f70404e66db5da7cf65feead4e3181c4fbbf9eb9   # origin/main

$ git merge-base pr-374 origin/main
d17750039a81d0cced5cf1a4fa7f0a5f05acc431

$ git log --oneline d17750039a81..origin/main | wc -l
251

$ git log --oneline d17750039a81..pr-374 | wc -l
16
```

The merge-base `d17750039a81` is `Merge PR #542 from fix/monitor-ha-pending-stuck`. Since divergence, `main` has accumulated **251 commits** while PR #374 has accumulated **16 commits**. PR #374's branch contains an internal "Merge main into branch" commit (`debd5449`), but that merge was not refreshed against the latest 251-commit run on main.

PR #374's 16 commits, oldest first (`be3836d0` Initial plan → `6fa37c19` Added connectivity properties to response models), include three CodeQL fixes (`d3005f12` resource injection, `8413dea6` log forging) and the `BackgroundService` rebuild (`48319b8b`).

### Q2 — Complete conflict list (`merge origin/main` → `pr-374`)

**Evidence (concrete git output, replayed in a clean clone):**
```
$ git merge --no-commit --no-ff pr-374    # from origin/main checkout
Auto-merging src/Dorc.Monitor/IMonitorConfiguration.cs
Auto-merging src/Dorc.Monitor/MonitorConfiguration.cs
Auto-merging src/Dorc.Monitor/MonitorService.cs
Auto-merging src/Dorc.Monitor/Program.cs
Auto-merging src/Dorc.Monitor/appsettings.json
Auto-merging src/Dorc.PersistentData/EntityTypeConfigurations/ServerEntityTypeConfiguration.cs
CONFLICT (content): Merge conflict in src/Dorc.PersistentData/EntityTypeConfigurations/ServerEntityTypeConfiguration.cs
Auto-merging src/Dorc.PersistentData/Model/Server.cs
CONFLICT (content): Merge conflict in src/Dorc.PersistentData/Model/Server.cs
Auto-merging src/Dorc.PersistentData/Sources/DatabasesPersistentSource.cs
Auto-merging src/Dorc.PersistentData/Sources/ServersPersistentSource.cs
Auto-merging src/dorc-web/src/pages/page-databases-list.ts
Auto-merging src/dorc-web/src/pages/page-servers-list.ts
Automatic merge failed; fix conflicts and then commit the result.
```

`git status --porcelain` after the merge attempt shows exactly **two files in `UU` (unmerged)** state:

| File | Conflict type | Characterisation |
|---|---|---|
| `src/Dorc.PersistentData/Model/Server.cs` | **Semantic** | PR #374 added `LastChecked`, `IsReachable`, `UnreachableSince` on the same `Server` POCO that PR #649 simultaneously renamed — the old `ICollection<Daemon> Services` collection was renamed to `Daemons`. Both branches modified the same line region. Resolution requires keeping main's renamed property name (`Daemons`) plus adding PR #374's three connectivity columns. Per SPEC §3, semantic conflicts require resolution rationale in the commit message. |
| `src/Dorc.PersistentData/EntityTypeConfigurations/ServerEntityTypeConfiguration.cs` | **Semantic** | Same root cause as the Model conflict. PR #374 added EF property mappings for the three connectivity columns; PR #649 changed the navigation-property name and surrounding shape. Resolution requires keeping main's mapping for the renamed `Daemons` collection plus adding PR #374's three new property mappings (LastChecked / IsReachable / UnreachableSince). |

**Auto-merged files (8) — listed for completeness; SPEC §3 requires no per-file rationale for these:**

`src/Dorc.Monitor/IMonitorConfiguration.cs`, `src/Dorc.Monitor/MonitorConfiguration.cs`, `src/Dorc.Monitor/MonitorService.cs`, `src/Dorc.Monitor/Program.cs`, `src/Dorc.Monitor/appsettings.json`, `src/Dorc.PersistentData/Sources/DatabasesPersistentSource.cs`, `src/Dorc.PersistentData/Sources/ServersPersistentSource.cs`, `src/dorc-web/src/pages/page-servers-list.ts`, `src/dorc-web/src/pages/page-databases-list.ts`.

The conflict surface is therefore **mechanically tractable**: 2 conflicts, both clustered around the same daemons-rename/connectivity-columns interaction, both resolvable by union-and-rename without inventing new behaviour. Note that the auto-merge result for the Monitor-side files is **textually clean but semantically suspect** — see Q4 — which is part of why the chosen route is revise, not ratify.

### Q3 — Daemons-modernisation (PR #649) interaction

**Evidence (reasoned analysis with citations):**

PR #649's renames span two surfaces of relevance to PR #374:
- `Server.Services` → `Server.Daemons` (Model.Server.cs at `f70404e6`).
- `ServiceStatus` → `DaemonStatusProbe` (one of #649's recent commits, see `git log --oneline 287347cf..f70404e6` which includes `ba08cc64 Rename ServiceStatus to DaemonStatusProbe (#649)`).

PR #374's branch (`6fa37c19`) was authored before #649 merged, so its files reference the old names. Specifically (verified with `git grep "Services"` and `git grep "ServiceStatus"` against `pr-374`):

- `src/Dorc.PersistentData/Model/Server.cs` at `6fa37c19` declares the `Services` collection (now `Daemons` on main). This is the `UU` from Q2.
- `src/Dorc.PersistentData/EntityTypeConfigurations/ServerEntityTypeConfiguration.cs` at `6fa37c19` configures the `Services` join (now `Daemons`). This is the second `UU` from Q2.
- The auto-merged files in Q2 do not reference daemons-rename identifiers, which is why they auto-merged (they touch Monitor-side wiring + persistent-source bodies that never collided with the rename).

The interaction is therefore **bounded to exactly the two `UU` files**, not pervasive. There is no `ServiceStatus`/`DaemonStatusProbe` reference anywhere in PR #374's diff (verified by `git grep -n "ServiceStatus\|DaemonStatusProbe" pr-374 -- src/`), so the second rename has no impact on this PR.

**Negative-claim evidence (per SPEC §4):** the `git grep` queries used were `git grep -n "ServiceStatus\|DaemonStatusProbe" pr-374 -- src/` and `git grep -n "\.Services\b" pr-374 -- src/Dorc.PersistentData/`; the only hits in PR #374's diff are the two `UU` files already enumerated.

### Q4 — Monitor-side DI / hosting / configuration changes since PR #374

**Evidence (reasoned analysis with citations):**

`git diff --stat d17750039a81..origin/main -- src/Dorc.Monitor/` shows main has changed `IMonitorConfiguration.cs`, `MonitorConfiguration.cs`, `MonitorService.cs`, `Program.cs`, and `appsettings.json` since the merge-base. PR #374 has changed the same five files. All five auto-merged textually (Q2), but **the resulting tree is not necessarily what the HLPS contract requires**:

- **`Program.cs`**: PR #374 adds two registrations:
  ```
  builder.Services.AddTransient<Dorc.Core.Connectivity.IConnectivityChecker, Dorc.Core.Connectivity.ConnectivityChecker>();
  builder.Services.AddHostedService<Dorc.Monitor.Connectivity.ConnectivityCheckService>();
  ```
  After the merge, both registrations land. But the second one registers a `BackgroundService`-derived class (verified: `git show pr-374:src/Dorc.Monitor/Connectivity/ConnectivityCheckService.cs` line 9 declares `public sealed class ConnectivityCheckService : BackgroundService`). Per HLPS U-2, this must be replaced with an `IHostedService` + `System.Threading.Timer` shape. The auto-merge therefore lands code that is textually clean but contractually wrong; revise must rebuild the host shape.

- **`MonitorService.cs`**: PR #374 adds `await Task.Yield();` at the top of `ExecuteAsync`. Main's current `MonitorService.cs` (at `f70404e6`) does not have this line at the equivalent location, so the merge inserts it without conflict. The reason PR #374 added the Yield is to let other hosted services start when `MonitorService` (a `BackgroundService`) blocks the host startup loop. This is a workaround for the very issue U-2's Timer-based redesign solves at root: with `IHostedService` + Timer, the Connectivity service does not block startup at all, so `MonitorService` does not need the Yield. **The Yield is therefore a candidate for removal under revise**, since the architectural reason for adding it disappears. (Removal is downstream-step work; S-001 only flags it.)

- **`IMonitorConfiguration.cs` / `MonitorConfiguration.cs` / `appsettings.json`**: PR #374's additions are `EnableConnectivityCheck` (bool) and `ConnectivityCheckIntervalMinutes` (int) on the contract, with corresponding implementations and JSON defaults. Main has had unrelated additions on these surfaces (RabbitMQ/HighAvailability config). Auto-merge lands both cleanly. These are usable as-is by both routes.

**Conclusion for Q4:** the textually-clean Monitor-side auto-merge masks a contractual mismatch in the host shape (`BackgroundService` vs U-2's Timer). This is a primary driver of the revise choice.

### Q5 — Schema state of `SERVER` and `DATABASE` on `main`

**Evidence (concrete git output):**
```
$ git show origin/main:src/Dorc.Database/dbo/Tables/SERVER.sql
CREATE TABLE [dbo].[SERVER] (
    [Server_ID]               INT IDENTITY (1, 1) NOT NULL,
    [Server_Name]             NVARCHAR (250) NULL,
    [OS_Version]              NVARCHAR (250) NULL,
    [Application_Server_Name] NVARCHAR (1000) NULL,
    CONSTRAINT [PK_SERVER] PRIMARY KEY CLUSTERED ([Server_ID] ASC)
        WITH (DATA_COMPRESSION = PAGE)
);

$ git show origin/main:src/Dorc.Database/dbo/Tables/DATABASE.sql
CREATE TABLE [dbo].[DATABASE] (
    [DB_ID]        INT IDENTITY (1, 1) NOT NULL,
    [DB_Name]      NVARCHAR (250) NULL,
    [DB_Type]      NVARCHAR (250) NULL,
    [Server_Name]  NVARCHAR (250) NULL,
    [Group_ID]     INT NULL,
    [Array_Name]   NVARCHAR (250) NULL,
    CONSTRAINT [PK_DATABASE] PRIMARY KEY CLUSTERED ([DB_ID] ASC) WITH (DATA_COMPRESSION = PAGE),
    CONSTRAINT [DATABASE_AD_GROUP_Group_ID_fk] FOREIGN KEY ([Group_ID]) REFERENCES [dbo].[AD_GROUP] ([Group_ID]),
    INDEX [IX_DATABASE_Server_Name_DB_Name] NONCLUSTERED ([Server_Name],[DB_Name])
);
```

Main's `SERVER` table has 4 columns and no `LastChecked`/`IsReachable`/`UnreachableSince`. Main's `DATABASE` table has 6 columns and no `CreateDate` (HLPS SC-9 territory) and no connectivity columns either. The diff `d17750039a81..origin/main -- src/Dorc.Database/dbo/Tables/SERVER.sql src/Dorc.Database/dbo/Tables/DATABASE.sql` is empty (no intervening edits to these files), so PR #374's structural delta is purely additive against main.

**Negative-claim evidence (per SPEC §4):** `git log --oneline d17750039a81..origin/main -- src/Dorc.Database/dbo/Tables/SERVER.sql src/Dorc.Database/dbo/Tables/DATABASE.sql` returned no commits — no intervening renames, no index changes, no constraint changes on these two tables. The schema delta path is structurally clean.

**Runtime-deferral citation (per SPEC §4):** whether the migration applies cleanly at runtime is **deferred to S-002** (per HLPS U-7 and IS S-002's verification intent: "dry-deploy to a non-production DOrc instance and verify migration apply"). S-001's structural answer is "no obstruction"; the runtime confirmation belongs to the next step.

### Q6 — Outstanding CodeQL review threads on PR #374

**Evidence (concrete API output via `gh api graphql`):**

Querying `repository(owner:"sefe", name:"dorc"){ pullRequest(number:374){ reviewThreads }}` returned **7 unresolved threads, all from `github-advanced-security`, all `isOutdated: true`**:

| Path | CodeQL alert |
|---|---|
| `src/Dorc.Core/Connectivity/ConnectivityChecker.cs` | Resource injection (alert 132) |
| `src/Dorc.Monitor/Connectivity/ConnectivityCheckService.cs` | Log entries from user input (alerts 133–138, six instances) |

The fixes are in tree at `pr-374`:
- `git log pr-374 --oneline -- src/Dorc.Core/Connectivity/ConnectivityChecker.cs` shows commit `d3005f12 Fix resource injection: use SqlConnectionStringBuilder for DB connection`. Verified by `git grep -n "SqlConnectionStringBuilder" pr-374 -- src/Dorc.Core/Connectivity/ConnectivityChecker.cs` returning a hit at line 48.
- `git log pr-374 --oneline -- src/Dorc.Monitor/Connectivity/ConnectivityCheckService.cs` shows commit `8413dea6 Fix CodeQL log-forging alerts: sanitize server/database names before logging`. The `SanitizeForLog` helper is present at line 152 of the file at `pr-374`.

Both fixes are in `pr-374`'s tree but the threads remain unresolved on GitHub because they were pinned to historical SHAs (hence `isOutdated: true`). They will either auto-resolve when CodeQL re-runs against the post-merge tip, or be moot under revise because S-005 (Timer-based service) replaces the file outright. **No force-push is required by S-001's merge** (regular merge commit), so SPEC §3 force-push handling does not apply; the thread state is captured here for the record regardless.

### Q7 — File-by-file salvageability vs the HLPS contract

Categorised against the HLPS success criteria (SC-1..SC-11) and unknowns (U-1..U-10). Files inspected: the 26 files in `git diff --stat d17750039a81..pr-374`.

| File | HLPS contract item(s) | Salvageable as-is? | Notes |
|---|---|---|---|
| `src/Dorc.Database/dbo/Tables/SERVER.sql` (+3 cols) | SC-1 (schema) | **YES** | Three columns are exactly what HLPS requires. |
| `src/Dorc.Database/dbo/Tables/DATABASE.sql` (+3 cols) | SC-1 (schema) | **YES** | Same. Note: SC-9 (`CreateDate`) is a separate column, not in PR #374 — handled by IS S-008/S-009. |
| `src/Dorc.PersistentData/Model/Server.cs` | SC-1 (model surface) | **YES with conflict resolution** | Three properties land cleanly once the daemons-rename `UU` is resolved (Q2). |
| `src/Dorc.PersistentData/Model/Database.cs` | SC-1 | **YES** | Auto-merged. Three new properties only. |
| `src/Dorc.PersistentData/EntityTypeConfigurations/ServerEntityTypeConfiguration.cs` | SC-1 (EF mapping) | **YES with conflict resolution** | Three property mappings land cleanly once the daemons-rename `UU` is resolved (Q2). |
| `src/Dorc.PersistentData/EntityTypeConfigurations/DatabaseEntityTypeConfiguration.cs` | SC-1 | **YES** | Auto-merged. |
| `src/Dorc.ApiModel/ServerApiModel.cs` (+3 nullable props) | SC-3 (API surface) | **YES** | Auto-merged. |
| `src/Dorc.ApiModel/DatabaseApiModel.cs` (+3 nullable props) | SC-3 | **YES** | Auto-merged. |
| `src/Dorc.PersistentData/Sources/Interfaces/IServersPersistentSource.cs` (+4 method signatures) | SC-2 (data access surface for connectivity update + batch read) | **PARTIAL** | `UpdateServerConnectivityStatus(int, bool, DateTime)` and `GetServersForConnectivityCheckBatch(int, int)` and `GetTotalServerCount()` are usable as-is. `GetAllServersForConnectivityCheck()` is dead code in batched flow — should be removed under revise. |
| `src/Dorc.PersistentData/Sources/ServersPersistentSource.cs` (impls + read-side hydration) | SC-2 + SC-3 | **PARTIAL** | The read-side hydration (3 properties added to existing API-model projections in `GetServers`, `GetAppServers`, `Get(int)`, `GetAllServers`) is reusable as-is. The `UpdateServerConnectivityStatus` impl correctly threads `UnreachableSince` (set on transition from reachable to unreachable; cleared on return to reachable). The `GetAllServersForConnectivityCheck` impl is dead and to be removed. |
| `src/Dorc.PersistentData/Sources/Interfaces/IDatabasesPersistentSource.cs` | SC-2 | **PARTIAL** | Same shape as Servers, same disposition. |
| `src/Dorc.PersistentData/Sources/DatabasesPersistentSource.cs` | SC-2 + SC-3 | **PARTIAL** | Same shape as Servers, same disposition. |
| `src/Dorc.Core/Connectivity/IConnectivityChecker.cs` | SC-4 (probe contract) | **PARTIAL** | The two-method shape (`CheckServerConnectivityAsync(string)`, `CheckDatabaseConnectivityAsync(string, string)`) is reusable, but per HLPS U-1 the server probe must internally do TCP/445 fallback — that change is in the impl, not the interface. |
| `src/Dorc.Core/Connectivity/ConnectivityChecker.cs` | SC-4 + U-1 | **NO — must be revised** | ICMP-only with 5s timeout. U-1 mandates TCP/445 (SMB) fallback when ICMP fails. The DB probe is acceptable as-is (uses `SqlConnectionStringBuilder`, IntegratedSecurity, 5s timeout). |
| `src/Dorc.Core.Tests/ConnectivityCheckerTests.cs` | testing contract | **PARTIAL** | The negative-input cases (null/empty/whitespace) and DB-invalid-connection cases are reusable. The `CheckServerConnectivityAsync_WithLocalhost_ReturnsTrue` test is environment-fragile (assumes ICMP is allowed in the test runner — fails in containerised CI by default) and the `WithInvalidServerName_ReturnsFalse` test will need updating once U-1 fallback lands. To be reworked under revise. |
| `src/Dorc.Monitor/Connectivity/ConnectivityCheckService.cs` | SC-5 (background driver) + U-2 | **NO — must be revised** | `BackgroundService` derivation is contractually wrong against U-2. The batched processing logic, the `Sanitize­ForLog` helper, the cancellation handling, and the per-cycle structure are all sound and can carry over conceptually into the Timer-based redesign — but the host-shape change is large enough that this is a substantive rework, not a patch. |
| `src/Dorc.Monitor/IMonitorConfiguration.cs` (+2 props) | SC-6 (config) | **YES** | Auto-merged; both properties (`EnableConnectivityCheck`, `ConnectivityCheckIntervalMinutes`) are reusable. |
| `src/Dorc.Monitor/MonitorConfiguration.cs` | SC-6 | **YES** | Auto-merged; impls reusable. |
| `src/Dorc.Monitor/MonitorService.cs` (+`await Task.Yield();`) | none directly | **REMOVE under revise** | The Yield workaround exists because PR #374's `BackgroundService` blocked startup. With Timer-based service per U-2, the workaround is unnecessary and should be reverted to keep main's pre-Yield shape. |
| `src/Dorc.Monitor/Program.cs` (+2 registrations) | SC-5 (DI) | **PARTIAL** | The `IConnectivityChecker` registration is reusable. The `AddHostedService<ConnectivityCheckService>` line is reusable in shape, but the registered type changes when the Timer-based service replaces the BackgroundService one. |
| `src/Dorc.Monitor/appsettings.json` (+2 keys) | SC-6 | **YES** | Auto-merged; the two defaults (`EnableConnectivityCheck=False`, `ConnectivityCheckIntervalMinutes=60`) are reusable. |
| `src/dorc-web/src/apis/dorc-api/models/ServerApiModel.ts` (+3 props) | SC-7 (UI model) | **YES** | Auto-merged; mirrors C# API model. |
| `src/dorc-web/src/apis/dorc-api/models/DatabaseApiModel.ts` (+3 props) | SC-7 | **YES** | Auto-merged. |
| `src/dorc-web/src/pages/page-servers-list.ts` (Status column + renderer) | SC-7 (UI surface) | **PARTIAL** | The grid-column wiring is reusable. The renderer logic (4-state: not-checked / online / unreachable-7d+ / offline) matches the SC-7 spirit, but uses raw inline styles, hard-coded colours, and bypasses the project's theme tokens (cf. PR #649 "Audit pages: derive highlight tokens from global theme via color-mix"). To be reworked under revise to use theme tokens. |
| `src/dorc-web/src/pages/page-databases-list.ts` | SC-7 | **PARTIAL** | Identical to the servers page; same disposition. |
| `CONNECTIVITY_MONITORING.md` | SC-10 (operator docs) | **PARTIAL** | The operator-facing description is largely correct against the HLPS, but it documents the BackgroundService impl shape and the ICMP-only probe — both items will need updating once U-1 / U-2 land. To be reworked under revise. |

**Salvageability summary:** of the 26 files, **11 are usable as-is (with conflict resolution where needed)**, **11 are partially salvageable**, and **3 require revision** (the two contract-violating files for U-1 and U-2, plus the Yield workaround which becomes redundant). No file is throwaway.

## 4. Route comparison

### Route A — Ratify (excluded by HLPS contract, kept for completeness)

- **Mechanical S-001 work:** merge `origin/main` into PR #374, resolve the 2 `UU` conflicts (Q2), commit, push. Same as revise.
- **Shape of S-002..S-011:** S-005 (the connectivity background driver) becomes a no-op verification step; same for U-1's TCP/445 fallback (S-003 in IS).
- **HLPS contract items requiring new code beyond what PR #374 contains** (per SPEC §5 unit):
  - **U-1**: TCP/445 fallback. PR #374 has ICMP-only. **NEW CODE REQUIRED.**
  - **U-2**: `IHostedService` + Timer. PR #374 has `BackgroundService`. **NEW CODE REQUIRED.**
  - **SC-9**: `CreateDate` column on DATABASE. PR #374 does not touch this. **NEW CODE REQUIRED** (S-008/S-009 territory regardless of route).
  - **SC-10**: docs reflecting the final architecture. PR #374's docs are pre-U1/U2. **REWRITE REQUIRED.**
  - **SC-11**: production verification. Always required. **NEW WORK regardless of route.**

  Five HLPS contract items still need new code under "ratify". Ratify therefore **does not exist** as a meaningful path under the current HLPS — choosing it would mean either silently dropping U-1 and U-2 (forbidden by CLAUDE.local.md §3.B "you are not allowed to fundamentally change / alter the scope of the current specification in the HLPS/IS") or admitting that "ratify" actually requires new code, which is the definition of revise.
- **Residual risk:** highest. Stale CodeQL threads stay until CI re-runs (Q6). Bot-authored UI patterns (raw colours, no theme tokens) leak into main if not reworked. The host-shape mismatch under U-2 silently propagates.

### Route B — Revise (chosen)

- **Mechanical S-001 work:** identical to ratify — merge `origin/main` into PR #374, resolve 2 `UU` conflicts, commit, push.
- **Shape of S-002..S-011:** S-002 (schema dry-deploy) and S-003 (ConnectivityChecker) and S-005 (Timer-based hosted service) and S-007 (UI rework with theme tokens) and S-010 (docs rewrite) all do real work. S-004 (persistent source) reuses PR #374's batched-read shape with minor cleanup (removing dead `GetAllServersForConnectivityCheck`). S-006 (API model) is largely a no-op — PR #374's API-model additions are reusable as-is.
- **HLPS contract items requiring new code beyond what PR #374 contains** (per SPEC §5 unit):
  - **U-1**: TCP/445 fallback. **NEW CODE.**
  - **U-2**: Timer-based host. **NEW CODE.**
  - **SC-7 theme-token alignment**: UI renderer rework. **NEW CODE.**
  - **SC-9**: `CreateDate` column. **NEW CODE.**
  - **SC-10**: docs rewrite. **NEW CODE.**
  - **SC-11**: production verification. **NEW WORK.**

  Six items require new work — one more than ratify, because revise is honest about the SC-7 theme-token rework that ratify would silently skip.
- **Residual risk:** moderate. Mechanical risk from the merge is low (only 2 semantic conflicts). Behavioural risk is bounded by the IS verification intents on each downstream step. Bot-authored patterns are explicitly addressed by the rework.

### Comparison table

| Dimension | Ratify | Revise (chosen) |
|---|---|---|
| Mechanical S-001 work | Merge + 2 conflict resolutions | Merge + 2 conflict resolutions |
| HLPS items requiring new code beyond PR #374 | 5 (forces silent dropping of U-1/U-2 to remain "ratify") | 6 (honest accounting incl. theme-token rework) |
| Shape of downstream IS steps | Several no-op steps; some verification-only | All 10 downstream steps do real work |
| Residual risk | High — silent contract violations | Moderate — bounded by IS step verification |
| Compatible with user directive (no supersede; all changes on existing PR) | Yes | Yes |
| Compatible with HLPS contract | **No** — drops U-1/U-2 | **Yes** |

## 5. Rejected alternatives

- **Ratify** (the non-chosen permitted route): rejected because PR #374's branch tip violates HLPS U-1 (no TCP/445 fallback) and U-2 (uses `BackgroundService`, not `IHostedService` + Timer). Choosing ratify would either silently drop those unknowns (forbidden by CLAUDE.local.md §3.B) or admit that ratify requires new code — at which point it is revise by another name. Rejection traceable to Q3 (boundary of rename impact), Q4 (host-shape mismatch in auto-merged Monitor surface), and Q7 (file-by-file salvageability showing 3 must-revise files).
- **Supersede** (close PR #374 and ship a fresh PR): excluded by **user directive 2026-04-28** ("all changes should be done on the existing PR"). Recorded for completeness; not comparatively analysed.

## 6. Branch-setup output (executed)

The branch setup completed on 2026-04-27 in the same S-001 cycle as the note draft. Sequence executed:

1. From a clean checkout of `pr-374` (tip `6fa37c19`), `git merge origin/main --no-ff --no-commit` produced the conflict surface enumerated in Q2 (2 `UU`, 8 auto-merged).
2. Conflicts resolved per Q2 characterisation:
   - `src/Dorc.PersistentData/Model/Server.cs`: kept main's `Daemons` collection name; preserved PR #374's three connectivity properties (`LastChecked`, `IsReachable`, `UnreachableSince`).
   - `src/Dorc.PersistentData/EntityTypeConfigurations/ServerEntityTypeConfiguration.cs`: kept main's `Daemons` mapping using the `deploy.ServerDaemon` join table (replacing the old `SERVER_SERVICE_MAP`); preserved PR #374's three property mappings (LastChecked / IsReachable / UnreachableSince).
3. Resolved files staged, merge committed as `7bfee34e1b4273a9455e1711e3a60ac2358cf0de` with message `Merge main into pr-374 (S-001 drift resolution)` and per-semantic-file resolution rationale in the body (per SPEC §3).
4. Pushed: `git push origin pr-374:copilot/create-server-db-existence-check` — fast-forwarded `6fa37c19..7bfee34e`, no force-push.
5. CI status on the new tip: to be confirmed in the Review History once GitHub Actions completes the run on `7bfee34e`. The pre-merge baseline on `origin/main` (`f70404e6`) was all-green (8/8 check-runs `success`); A4's bar is therefore "all required checks green at post-merge tip".
6. CodeQL threads (Q6) state: unchanged on PR #374 since no force-push occurred. Re-verification post-CI-run is a downstream housekeeping action; threads were already `isOutdated: true` before the push and the underlying fixes (commits `d3005f12`, `8413dea6`) remain in tree at `7bfee34e`.

**Force-push consent (per SPEC §3 / §10):** **NOT TRIGGERED** — the merge produced a fast-forward push (`6fa37c19..7bfee34e`). PR #374's review-comment / CodeQL-thread state is therefore unaffected by S-001's branch setup; nothing was silently reset.

**Acceptance against A4:** the post-merge tip exists, has `origin/main` in its ancestry (verified: `git merge-base --is-ancestor origin/main 7bfee34e` returned 0), and has no outstanding conflicts. CI green-light at the new tip remains pending the GitHub Actions run; the run is in flight at the time of submission. If the run reveals new failures vs the all-green main baseline, S-001's acceptance is held until the regression is diagnosed (per SPEC §10 risk row "merge errors that pass CI").

## 7. Implications for S-002..S-011

| Step | What S-002+ author needs to know |
|---|---|
| **S-002** (schema dry-deploy) | The post-merge tip already contains the +3 columns on each of `SERVER` and `DATABASE`. Dry-deploy verifies migration apply on a non-production instance per HLPS U-7. Note: `DATABASE.CreateDate` (SC-9) is **not** in this delta — that lands in S-008. |
| **S-003** (ConnectivityChecker U-1 fallback) | The class to revise is `src/Dorc.Core/Connectivity/ConnectivityChecker.cs`. The interface `IConnectivityChecker` does not need to change — the fallback is internal. Tests in `src/Dorc.Core.Tests/ConnectivityCheckerTests.cs` need rework (the `WithInvalidServerName_ReturnsFalse` case will need a non-listening port + non-pingable host to remain meaningful; the localhost case is environment-fragile and should be replaced with a deterministic check). |
| **S-004** (persistent source) | PR #374's `UpdateServerConnectivityStatus` / `UpdateDatabaseConnectivityStatus` and the batch readers are usable. Remove `GetAllServersForConnectivityCheck` and its database-side equivalent — dead code in the batched flow. The read-side API-model hydration in `GetServers`, `GetAllServers`, `GetAppServers`, `Get(int)` for both sources is reusable. |
| **S-005** (Timer-based hosted service per U-2) | Replace `ConnectivityCheckService : BackgroundService` with `IHostedService` + `System.Threading.Timer`. The batched per-cycle logic, the `SanitizeForLog` helper, and the cancellation pattern all carry over conceptually. Update `Program.cs` registration accordingly (still `AddHostedService<...>`). Revert `await Task.Yield();` in `MonitorService.cs` once Timer is in place — main's pre-Yield shape is the target. |
| **S-006** (API model) | Largely no-op. `ServerApiModel.cs` and `DatabaseApiModel.cs` already carry the three nullable properties; UI TS models mirror them. |
| **S-007** (UI rework) | Rework the `connectivityStatusRenderer` in both `page-servers-list.ts` and `page-databases-list.ts` to use theme tokens (cf. PR #649 audit-pages pattern: derive highlight tokens from global theme via `color-mix`). Replace inline `style="color: green;"` with token-based equivalents. The 4-state semantics (not-checked / online / unreachable-7d+ / offline) are sound; only the styling vehicle changes. |
| **S-008 / S-009** (DATABASE.CreateDate) | Greenfield against PR #374 — no salvageable code. PR #374's diff does not touch `CreateDate`. |
| **S-010** (docs) | Rewrite `CONNECTIVITY_MONITORING.md` once U-1 / U-2 land — the current draft documents BackgroundService and ICMP-only, which is wrong against the final shape. |
| **S-011** (production verification) | Naturally last; no S-001 inheritance. |

**Inherited risks for downstream steps:**
- The daemons-rename touched `Server.cs` / `ServerEntityTypeConfiguration.cs` — downstream steps modifying these files must keep main's `Daemons` name (do not regress to `Services`).
- `MonitorService.cs`'s `await Task.Yield();` is a workaround introduced by PR #374; S-005 must remove it when the Timer-based service lands.
- CodeQL threads (Q6) are outdated and will re-fire on the new tip if the underlying issues recur. The fixes are in tree — keep them in tree under revise.

---

## 8. Review History

(empty — this is R1 DRAFT prior to first submission)
