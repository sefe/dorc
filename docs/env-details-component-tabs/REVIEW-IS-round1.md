# Adversarial Review — IS env-details-component-tabs — Round 1

Panel: 2 independent reviewers — (a) coverage/traceability against HLPS v5;
(b) ordering/atomicity/executability against the repo.
Verdict: REVISE. 2 HIGH (one shared), 5 MEDIUM, 5 LOW. All applied in IS v2
(steps renumbered S-001..S-008 after the merge).

| ID | Sev | Finding (abridged) | Triage | Resolution in IS v2 |
|----|-----|--------------------|--------|----------------------|
| G1/H1 | HIGH | S-002 (SSDT) / S-003 (EF) split contradicts HLPS §4 ("same step") — creates the committed drift state R-4 exists to prevent, and leaves S-002 with no meaningful gate | Accept (merged finding) | Former S-002+S-003 merged into new S-002 "Schema, dual-sourced"; gate is the DDL↔configuration comparison at one boundary |
| H2 | HIGH | Client-regen step consumed S-001's toolchain record without depending on it — graph permitted scheduling before the record existed | Accept | S-001 added to S-006's Depends On (index + body) |
| G2 | MED | R-5 name-collision check (HLPS commitment "during the client-regen step") had no owning step | Accept | S-006 verification gains explicit R-5 gate; index updated |
| G3 | MED | U-11's full-stack half (API+DB+web together, deciding SC-2's evidence mode) not verified at pre-flight | Accept | S-001 records full-stack executability and fixes SC-2's evidence mode up front; S-007 consumes the decision |
| H3 | MED | "Characterization test still passes unmodified" unsatisfiable — constructor grows, wiring must change | Accept | Reworded: call-set assertions unchanged; only stub wiring for new sources may be added |
| H4 | MED | Duplicate-attach verification not executable at the sources step's boundary under the mocked-context test pattern | Accept | S-003 specifies a behavioural exists-check (mock-testable); PK backstop explicitly deferred to S-004's clean-4xx test |
| H5 | MED | Monitor-side R-6 gate rested on an integration suite needing a real DB, unverified | Accept | R-6 checks specified as DB-free container-resolution tests; integration-suite executability added to S-001's record |
| G4 | MED | Step-index "Addresses" inaccuracies (SC-4/SC-2 on sources step; U-8 on schema; R-4 on final sweep; U-11 on UI) | Accept | Index corrected across all steps |
| G5 | LOW | Variable-DTO placement silently resolved an HLPS §3 vs §5.7.1 discrepancy | Accept | S-005 names the discrepancy and the §5.7.1 rationale (runner-side type resolution) |
| G6 | LOW | U-9 veto point (typed attach/detach) not surfaced at the IS-approval checkpoint | Accept | Header note + S-004 body flag this approval as the designated veto moment |
| H6/H7 | LOW | S-004 (~24 endpoints) and S-007 (12 components) too big for one review pass | Accept (downgraded remedy) | Pattern-first execution: Containers vertical sets the pattern, other two replicate, two-pass review inside one step — avoids renumbering into thin gate-heavy steps |
| H8 | LOW | S-001 bundles characterization + toolchain concerns | Accept as-is | Kept combined as pre-flight; the H2 edge that made bundling risky is now mandatory |

Verified positives recorded by the panel: sources step compiles/gates independently of
controllers; controller tests need no client regen; integration track's dependency set
(S-001+S-003) is truly minimal; two-track parallelism holds (no variable DTO appears in
the generated client spec, so track order cannot perturb the SC-5 diff); the S-006→S-007
chain is the only legal UI path; final-sweep dependencies transitively cover all steps.

Rejected: none. Deferred: none.
