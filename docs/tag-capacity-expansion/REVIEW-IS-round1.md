# Adversarial Review — IS tag-capacity-expansion — Round 1

Panel: 2 independent reviewers — (a) coverage/traceability vs HLPS v2; (b) ordering/
atomicity/executability vs the repo and the delivered exemplar IS.
Verdict: REVISE. 2 HIGH, 4 MEDIUM, 4 LOW. All applied in v2 (steps renumbered
S-001..S-006 after the UI split).

Facts the panel verified for the IS: the four-field shared `maxFieldLength=50`; the
`DatabaseEntityTypeConfiguration` four-properties-at-50 shape; typescript-rxjs models
carry no validation metadata (in-repo precedent: `PropertyFilter.maxLength` in spec,
bare interface in the generated model); `server-tags` opens from grid-button dialogs
(`manage-server-tags`) — the database grids have no equivalent integration point today;
no test on this branch depends on the old EF widths (S-002 leaves the tree green).

| ID | Sev | Finding (abridged) | Triage | Resolution in v2 |
|----|-----|--------------------|--------|-------------------|
| O1 | HIGH | "Single shared constant for N across API and UI" impossible across the C#/TS boundary; SC-2's one-symbol reduction overpromised | Accept | S-003 states the real mechanism: C# constant (attributes) + TS constant (UI) + a web test parsing `swagger.json` to prove `maxLength` = TS constant |
| V1 | HIGH | HLPS §3.3 consumer re-verification (resolver, daemons projection) unowned by any step/SC | Accept | S-003 owns it: near-N unit tests per consumer; S-006 summarizes results |
| O2 | MED | Spec-splice workflow is a cross-branch artifact absent here; S-003's mechanism could be missing at execution | Accept | S-001 item 3 checks feasibility; S-003 carries an explicit fallback |
| V2 | MED | "`DatabaseEntityTypeConfiguration` HasMaxLength(50)→(N)" ambiguous — four properties at 50; over-broad reading overflows the unique index on EnsureCreated DBs | Accept | S-002 names `ArrayName` only, explicitly freezes the other three, and the gate + model test check them |
| V3 | MED | U-4's unmerged-path follow-up item had no owner/landing place | Accept | S-001 creates the item; S-006 carries it into release note + PR description |
| O3 | MED | Mirror clause determinable and resolves to *new* grid integration work; single UI step too fat for one gate | Accept | Conditional resolved (databases get the grid-launched tags dialog); UI split into S-004 (editor+enforcement+limits) and S-005 (grid integration+relabel+rendering) |
| V4 | LOW | Step-index Addresses column drifted from bodies | Accept | Matrix rebuilt against v2 bodies |
| V5 | LOW | Mirror clause should be resolved, not carried | Accept | Same resolution as O3 |
| O4 | LOW | Strict-sequence rationale overstated ("never accepts more than schema beneath" is false today — API has no validation) | Accept | Rationale rewritten honestly; N-independent UI work noted as sequential-by-choice |
| O5 | LOW | `DeploymentContextModelTests` pattern reference is cross-branch | Accept | S-002 carries the pattern inline (offline model build via static-flag workaround) |

Rejected: none. Deferred: none.
