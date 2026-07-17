# S-006 — Final Verification Sweep (database-tags, 2026-07-17)

## Suites vs S-001 baselines

| Suite | Baseline | Final |
|-------|----------|-------|
| `Dorc.Core.Tests` | 140 | **150/150 pass** (+4 resolver characterization → membership, +5 new membership/casing/padded/verbatim, +1 translation artifact) |
| `Dorc.Api.Tests` | 218 pass / 22 platform | **241 pass / same 22** (+9 source characterization → membership incl. gate F-1/F-2 fixtures, +2 boundary, +9 lookup-param, +… net +23) |
| `Dorc.Monitor.Tests` | 107 pass / 4 platform | unchanged (resolver consumed via interfaces; no Monitor code touched) |
| `dorc-web` (chromium) | 121 / 14 files | **136/136 pass / 16 files** (+2 agreement, +6 tag-parser, +8 components, header-selector update) |
| Web build | clean | clean |

## Success criteria

- **SC-1 (invariance at the unit seam)** ✅ — single-value characterizations (S-001)
  pass unchanged post-rewrite; exactly the two declared groups flipped (multi-tag
  misses → membership; site-7 null NRE → skip), both flips pre-declared in SPEC-S-001
  and re-verified by the independent S-001..S-003 gate. Admitted exclusions
  (SQL-level padding parity, pre-existing multi-tag rows) route to U-2/U-7.
- **SC-2 (membership)** ✅ — `Endur;Reporting`-style fixtures verified at sites 1–6
  (both `GetDatabaseByType` overloads, users join, permissions filter, config path,
  resolver fixed lookups), site 9 (ThinClient vitest), and site 7 emits all four
  per-tag variables with scalar/array shapes verified in both families.
- **SC-3 (layer agreement + translation)** ✅ — EF width test (`Type` = 4000, frozen
  fields locked); spec↔UI agreement test (server + database maxLength); DDL↔EF
  side-by-side in SPEC-S-002; `ToQueryString` SQL artifact in SPEC-S-003,
  independently re-captured byte-identical by the gate.
- **SC-4 (boundary)** ✅ — 4000 accepted / 4001 rejected naming member + limit;
  `;`-bearing lookup params rejected as 400 with the absent-param regression proving
  omission keeps no-filter semantics; params trimmed at the boundary (gate F-3).
  Empty/whitespace `dbType` over real HTTP **binds as omitted** (no filter — safe by
  construction via the source's null guard); the in-action empty-check is
  defense-in-depth for direct callers (final gate F-B; detail in SPEC-S-004).
- **SC-5 (UI)** ✅ — all five assertions green in vitest: chip round-trip; over-limit
  visible rejection with zero API calls; exactly-4000 accepted; attach-database
  overlap warning naming the overlapping tag(s) (single-value behaviour unchanged);
  env-control-center ThinClient tag-membership resolution. Relabel verified.
- **SC-6 (baselines)** ✅ — table above; the 22 Api / 4 Monitor failures are the
  pre-existing platform group, byte-for-byte the same tests as the baseline.

## Operational scripts

- **U-2**: `Dorc.Database/Scripts/Post-Deployment/NormalizeDatabaseTags.sql`, wired
  into `Script.PostDeployment.sql` and the sqlproj. Compat-100-safe (CHARINDEX walk,
  no STRING_SPLIT), idempotent, order-preserving, binary-collation keep-first dedup,
  all-dropped → NULL — `TagString.Normalize`'s rules with one scoped difference
  (final gate F-C): T-SQL LTRIM/RTRIM trim **spaces only**, exactly the class SQL
  `=` used to forgive; tab/CR/LF-padded entries never matched before, are no
  regression, and converge on the next application write.
- **U-7**: `install-scripts/AuditDatabaseTags.sql`, read-only, three reports
  (multi-tag rows; padded rows; per-environment tag collisions via a recursive-CTE
  splitter). Run before deploy.

## Evidence routed off-box (no SQL Server / no sqlproj build here)

1. **Dacpac build** of the widened column, proc parameter, and the new post-deploy
   script — CI.
2. **U-2 script execution** — CI dacpac publish + one user-environment run;
   the script was line-by-line panel-reviewed here (compat-100 constraint the key
   review item).
3. **Live round-trip** — in a deployed environment: save a multi-tag database
   (e.g. `Endur;Reporting`) through the chip editor, confirm `ByType?type=Reporting`
   finds it, and confirm a deployment resolves `DbServer_Reporting`/`DbName_Reporting`.

## Rollout notes

1. **Audit first (U-7)**: run `AuditDatabaseTags.sql` against prod before deploying —
   multi-tag rows change matching behaviour at deploy; collisions make
   `GetDatabaseByType`-style resolution throw (kept U-1 behaviour, now per-tag).
2. **Dacpac first (C-2)**: the widened schema + normalization script deploy before
   the new API/UI. The API must not ship without the S-004 rejection (gate F-4) —
   satisfied automatically by single-PR delivery.
3. **Variable shape rule**: when a database gains multiple tags, each tag emits
   `DbServer_<tag>`/`DbName_<tag>`; a tag shared by several databases emits arrays
   instead of scalars. `DatabasePermissions[].Database.Type` carries the raw joined
   string verbatim.
4. The databases-list filter keeps its substring semantics (documented, unchanged).
