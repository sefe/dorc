# HLPS Review — Round 2 (delta, 2026-07-17)

Single delta reviewer, checklist = the 18 triaged rows of REVIEW-HLPS-round1.md plus a
hunt for revision-introduced defects. Every new factual assertion in the v2
dispositions was re-verified against code (site 9 line/comment, site 7 exact range and
both emission families, `VariableValueDbPerm` equality members, the
`add-edit-database` input pattern, RefreshEndur consumption, all width claims).

**Result: all 18 rows RESOLVED.** Cross-consistency checks passed (SC-1 exclusions ↔
§2.5/§3 deltas; U-7 phrasing; §5.6 script coverage; dedup-vs-collation policy).

## New findings

- **NEW-1 (MEDIUM, accepted)**: empty-string lookup param inverts the null contract —
  after the rewrite, `type=""` yields needle `";;"` which equals the COALESCE'd
  haystack of a null-`Type` row, so it would match every untyped database (today it
  matches nothing). Site 3 already guarded by `IsNullOrEmpty`. **Fix applied (v3)**:
  null/empty/whitespace lookup params rejected as 400 alongside `;`-bearing ones
  (§3, §5.4, SC-4); `TagString.HasTag` returns false for null/empty/whitespace.
- **NEW-2 (LOW, accepted)**: U-2 script omitted the dedup step that write-normalization
  applies; §5.6's audit summary omitted padded rows. **Fix applied (v3)**: both lists
  aligned.

## Verdict

**APPROVE** (conditional edits folded in as v3). Lifecycle: panel approval granted;
document APPROVED. Next: user checkpoint resolves U-2 (normalization script), U-4
(relabel), U-6 (PR scoping) and acknowledges U-7 (pre-deploy audit) before the IS.
