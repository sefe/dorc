# Step Gate Log — database-tags

## S-001..S-003 — characterization, schema, matching (commits 694e359, 7c2b2a4, 8b91903) — PASS with follow-ups

Gate re-verified independently: the frozen call-set matches the HLPS survey (sites
1–7, both variable families); the S-002 diff touches exactly the three declared
lines with the width test moved correctly; every HLPS §3 semantic position was
checked against the S-003 diff; the `ToQueryString` artifact was **re-captured live
and matched SPEC-S-003 byte-for-byte**; the flip contract was honoured exactly;
suite counts reproduced in a clean worktree. The `%`/`_` wildcard concern was
verified a non-issue (EF parameterizes + escapes the needle — LIKE behaves
literally, same as `Contains`).

Findings triage:

- **F-1 MED** (SPEC-S-003 overclaimed a site-5 collision fixture) → **fixed**:
  `GetConfigurationFilePath_NullTypeExcluded_AndSharedTagThrows` adds the U-1
  collision-throw fixture at site 5.
- **F-2 MED** (IS-mandated null-`Type` fixtures missing at sites 1–5) → **fixed**:
  `NullTypeDatabases_AreExcludedFromTagLookups` (sites 1/4),
  `..._AndSharedTagThrows` (site 5), and
  `NullTypeDatabases_AreInvisibleToUsersJoinAndPermsFilter` (sites 2–3).
- **F-3 LOW** (parameter-trim asymmetry: `TagString.HasTag` trims the needle, the
  EF pattern doesn't) → **fixed at the boundary**: both controllers trim the lookup
  param after validation, so both matching families receive identical needles;
  internal callers pass literal tags.
- **F-4 LOW** (interim NEW-1 exposure at 8b91903: empty needle unguarded) →
  **closed by S-004** (rejection landed in the same PR before any release point);
  rollout notes will state the API must not be deployed without S-004.
- **F-5 LOW** (deleted whole-string site-3 assertion left `;`-needles untested
  between commits) → **closed by S-004's** `;`-rejection tests.

## S-004 — API validation (commit 2285598) + S-005 — UI

Boundary evidence: 4000/4001 with member+limit message (both models); lookup-param
rejection incl. the absent-vs-empty regression (`GetUserDbPermissions("s1","db1")`
reaches the source as null); splice diff = 2 lines mirroring the server fragment;
spec↔UI agreement test extended and green. S-005 evidence: SC-5's five assertions
green in vitest (chip round-trip, over-limit visible rejection with zero API calls,
exactly-at-limit accept, overlap warning naming the tag, ThinClient membership);
relabel verified (header selector test updated + green); render sweep green
(responsive-grids 24/24); web 136/136; build clean. Formal S-004/S-005 panel review
runs with the S-006 final sweep.

## S-004..S-006 — final gate (diff 8b91903..5fd9c35) — REVISE → fixed

Gate independently re-verified: all five S-001..S-003 follow-up closures real; suite
counts reproduced; both SQL scripts line-by-line sound on the critical checklist
(compat-100 legality, idempotence incl. the `'a'` no-op and `' ; ; '`→NULL cases,
no SUBSTRING truncation, genuinely-binary keep-first dedup, batch safety after the
`GO`-terminated predecessor, splitter termination on all value shapes, MAXRECURSION
placement, join names vs DDL and vs the EF relationship the runtime resolves
through); SC-5 assertions non-vacuous; swagger splice minimal; RefreshEndur
unaffected.

Findings triage:

- **F-A HIGH** (chip rebuild storm: hosts bind fresh arrays each render, the tags
  setter rebuilt chips unconditionally, and real Tagify fires add events on
  programmatic addTags — cascading into per-keystroke network calls; invisible to
  the suite because FakeTagify has no event API) → **fixed**: dirty-check in the
  `tags` setter (no-op when the chip set is unchanged) + a regression test that
  counts rebuilds across an unrelated host re-render; `edit:updated` also wired so
  in-place chip edits reach live validation (closes LOW-3).
- **F-B MED** (supplied-empty `dbType` 400 unreachable over HTTP: the binder turns
  `?dbType=`/whitespace into null → no-filter 200; docs claimed otherwise) →
  **fixed by documentation**: SPEC-S-004 and VERIFICATION SC-4 now state
  empty-binds-as-omitted, safe by construction via the source's null guard, with
  the in-action check recorded as defense-in-depth for direct callers.
- **F-C MED** (normalization script's LTRIM/RTRIM is space-only vs TagString's
  all-whitespace Trim; header overclaimed "same rules") → **fixed by scoped
  documentation** in the script header and VERIFICATION: space-only is exactly the
  class SQL `=` forgave; other whitespace never matched before and converges on the
  next application write.
- **LOW 1** (chip editor alphabetizes stored tag order via splitTags' sort) →
  **accepted**: HLPS records order as display-only; noted here for the record.
- **LOW 2** (relabel evidence partial) → **fixed**: the dialog's tags-input label
  is now test-asserted alongside the grid header.
- **LOW 4** (audit Report 3 groups under DB collation — over-reports vs Ordinal
  sites; cursor could be STATIC) → **accepted**: conservative over-reporting is the
  right direction for a pre-deploy audit; both noted for any future revision.

Post-fix verification: web **137/137** (regression test added), build clean; .NET
suites unaffected by the fix (UI-only + docs). Gate closed.
