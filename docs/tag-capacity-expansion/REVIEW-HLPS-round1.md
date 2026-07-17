# Adversarial Review — HLPS tag-capacity-expansion — Round 1

Panel: 2 independent reviewers — (a) completeness/factual accuracy against this branch
(main-based; #773 not present here); (b) process compliance & decision quality against
CLAUDE.md and the delivered exemplar.
Verdict: REVISE. 2 HIGH, 4 MEDIUM, 10 LOW. All applied in v2.

Both reviewers re-verified the factual base independently: the §1 layer table, the proc
parameter, the shared `maxFieldLength=50` across four fields, the consumer survey, the
index-math correction, `[ApiController]` presence, and the U-3 pass-through resolution
all confirmed accurate.

| ID | Sev | Finding (abridged) | Triage | Resolution in v2 |
|----|-----|--------------------|--------|-------------------|
| P1 | HIGH | No Success Criteria section — delivery unfalsifiable | Accept | §6 SC-1..SC-6 (boundary N/N+1 per endpoint with named suites, layer-agreement check, >old-ceiling round-trip, five rendering surfaces, regression baselines, chip-editor criterion) |
| P2 | HIGH | No Constraints section; dual-source same-step schema constraint, toolchain limits, and client-regen constraint not inherited | Accept | §4 added with all four |
| P3/C1 | MED | U-4 incoherent: scope hard-codes five columns while the three component columns don't exist on this branch and the dependency was marked non-blocking with no fallback | Accept (merged) | U-4 rewritten with an explicit executable fallback (two columns now + follow-up on merge; five if merged); §5.2 conditioned on it |
| P4 | MED | Live rollout ordering unaddressed (API limits before dacpac reintroduces the failure mode) | Accept | §4 ordering constraint + R-1 |
| C2 | MED | "Effective limit is the lowest layer" wrong — EF `HasMaxLength` is metadata, not a save-time validator; effective limits are 1000/250(+UI 50), and EnsureCreated DBs get 250/50 columns (drift is live today) | Accept | §1 enforcement paragraph rewritten; feeds R-2 |
| P5 | MED | Carry-over finding 7's either/or had no owner/decision point | Accept | U-5 registered (default accept & document, user may veto) |
| P6 | LOW | U-2 bundles two decisions | Accept | Split U-2a/U-2b |
| P7 | LOW | U-1 lacks sizing evidence | Accept | §5.1 (widest layer 1000; no consumer needs >4×) |
| P8 | LOW | Missing Out-of-scope/Risks/Alternatives | Accept | §7/§9/§10 |
| P9 | LOW | Chip-editor maxlength undefined (`tags-input` has no single input) | Accept | §5.4: enforce on the joined string; IS defines the point |
| C3 | LOW | `usp_Insert_Server_Detail` has no in-repo callers — widen-vs-delete question unrecorded | Accept | U-6 registered (default widen when liveness unknowable) |
| C4 | LOW | Rendering survey missed `attach-server.ts` | Accept | Finding 6 now lists five surfaces |
| C5 | LOW | `RefDataAppServers.feature` (appserv filter) uncited for finding 7 | Accept | Findings 7/8 + SC-5 |
| C6 | LOW | `DataAccessor.cs` out-of-scope status worth recording | Accept | Finding 8 parenthetical |

Rejected: none. Deferred: none.
