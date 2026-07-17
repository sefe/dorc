# HLPS Review — Round 1 (2026-07-17)

Panel: 3 independent reviewers — R-A (survey completeness & factual accuracy),
R-B (semantic soundness & behavioural risk), R-C (testability & process).
All three verdicts: **REVISE**. Notably, R-B and R-C independently converged on the
two central HIGHs (trailing-space semantics; `DbName_<type>` omission), and R-A/R-B/R-C
all found the site-7 `DbName_` omission independently.

## Triage (Accept / Downgrade / Defer / Reject)

| # | Source | Sev | Finding | Triage | v2 disposition |
|---|--------|-----|---------|--------|----------------|
| 1 | R-B F-1 | HIGH | "Opt-in" claim false: pre-existing `;`-rows change behaviour at deploy; SC-1 framing dodges them | **Accept** | §2.5 rewritten; new U-7 pre-deploy audit; R-1 rewritten around audit + shape change |
| 2 | R-B F-2 / R-C F-1 | HIGH | SQL `=` ignores trailing spaces, delimiter pattern doesn't → padded single-value rows silently stop matching; U-2 scoped to miss them; unit seam can't prove SQL parity | **Accept** | §3 trailing-space paragraph; U-2 widened to ALL rows; SC-1 rescoped to unit seam with admitted exclusions; §10 records rejected query-side `Trim()` |
| 3 | R-A F-1 | HIGH | Survey missed `env-control-center.ts:319` ThinClient whole-string match | **Accept** | Site 9 added to §1, §2.1, §5.5, SC-2, SC-5 |
| 4 | R-A F-2 / R-B F-4 / R-C F-2 | HIGH | Site 7 also emits `DbName_<type>` (:122–135); survey/outcome/SCs covered only `DbServer_` | **Accept** | Site 7 range corrected to :100–136; both families in §2.2, SC-1, SC-2, R-1 |
| 5 | R-B F-3 | MED | U-5 conceals pre-existing overload casing divergence; no in-value casing/dedup policy | **Accept** | §3 documents inherited divergence + Ordinal dedup policy; §7 excludes fixing it |
| 6 | R-A F-3 (rel. R-B F-4) | MED | `DatabaseDefinition.Equals`/`DatabasePermissions` carries raw `Type`; no position taken | **Accept** | §1 projection-consumer row; §3 verbatim-pass-through position; release-note item |
| 7 | R-B F-5 | MED | Write-normalization silent on order/dedup/empty/rewrite-on-update | **Accept** | §3 normalization rules: order preserved, Ordinal dedup keep-first, all-dropped→NULL, update-rewrite noted |
| 8 | R-B F-6 | MED | Lookup param containing `;` becomes adjacent-sublist matching; no tag-charset invariant | **Accept** | §3 invariant (`;` banned in a tag); 400 on `;`-bearing lookup params (SC-4, §5.4) |
| 9 | R-A F-4 | MED | UI pattern already forbids `;` — "half-believes" premise overstated; chip editor must define per-tag charset | **Accept** | §1 notes editor cannot create multi-tag today (API/SQL can); §2.4 retains per-tag charset |
| 10 | R-A F-5 / R-B F-7 | MED/LOW | Null-`Type` contract unstated; site 7 crash→skip is an SC-1 deviation | **Accept** | §3 null/empty contract; deviation admitted in §3 and SC-1 |
| 11 | R-C F-3 | MED | EF-translatability claim unfalsifiable in-plan; `ToQueryString` on the offline context is available evidence | **Accept** | §3 evidence form; SC-3 artifact; §5.3 |
| 12 | R-C F-4 | MED | attach-database overlap change had no success criterion | **Accept** | SC-5 extended |
| 13 | R-A F-6 | MED | RefreshEndur CLI consumes the ByType endpoint — impact unlisted | **Accept** | §1 site 4 row; U-1/R-2 mention restore-tooling failures |
| 14 | R-B F-8 | LOW | Permissions `dbType` swagger description also needs membership-semantics update | **Accept** | §5.4 |
| 15 | R-C F-5 | LOW | SC-4 "readable message" weasel wording | **Accept** | SC-4 names member + limit, mirrors prior message-shape test |
| 16 | R-A F-7 | LOW | "Three grids split" — only two components (on three pages) | **Accept** | §1 reworded |
| 17 | R-A F-8 | LOW | `DataAccessor.cs` test insert has its own 250 width | **Accept** | §1 capacity-chain note |
| 18 | R-C F-6 | LOW | Site-8 line drift (:158–166 vs :156–163) | **Accept** | Corrected |

Rejected: none. Deferred: none.

## Round-1 verified-accurate notes worth keeping

- All eight originally-surveyed sites' execution-mode labels confirmed by two reviewers
  (site 3 EF-translated pre-`ToList`; site 4 in-memory post-materialization).
- EF Core 8 translates the delimiter pattern in both required query shapes; NULL is
  safe via COALESCE-wrapped concat (outcome equals today's `NULL == tag`).
- U-1 four-site `SingleOrDefault` throw claim accurate; site 2 correctly excluded.
- Unit seam exists for sites 1–5 (`DbContextMock` + `IDeploymentContext`) and 6–7
  (interface-injected resolver) — SC-1 achievable at that seam.
- No further backend whole-string matches; no PowerShell/Monitor/Runner/k6 consumers.

## Outcome

v2 published (this commit). Next: round-2 delta review of the v2 changes, then user
checkpoint (U-2, U-4, U-6, U-7 acknowledgement).
