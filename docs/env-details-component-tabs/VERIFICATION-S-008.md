# S-008 — Final Verification Sweep (2026-07-17)

## Test evidence (this environment; baselines from TOOLCHAIN-S-001)

| Suite | Result | Baseline delta |
|-------|--------|----------------|
| `Dorc.Core.Tests` | **149/149 pass** | +11 new (2 characterization, 2 model, 6 component-variable, 3 serialization... net) — zero failures |
| `Dorc.Api.Tests` | **314 pass / 22 fail** | The 22 failures are the pre-existing "Windows Principal not supported on this platform" set, byte-identical to the pre-change baseline (213→314 passes = +101 new tests, all green) |
| `Dorc.Monitor.Tests` | **109 pass / 4 fail** | Same 4 pre-existing platform failures as the clean-tree baseline; +2 new resolution tests green |
| `dorc-web` (chromium project) | **133/133 pass** | +15 new component tests; firefox/webkit not installable here (baseline) |
| `Tests.Acceptance` | compiles clean | Runtime execution needs a deployed stack (U-10: no in-repo feature touches the changed paths; `RefDataServers.feature` untouched by this change) |

## Success criteria

- **SC-1** ✅ — `grep -rn "will be implemented here" src | wc -l` → **0**. All three routes
  render grid-based tabs (component tests assert per-type columns and controls).
- **SC-2** ◐ — create/attach/edit/detach/delete verified end-to-end at the two levels
  available here: controller tests (every endpoint, privileged + unprivileged) and
  component tests (controls, gating, form binding). Full stack is not runnable in this
  environment (TOOLCHAIN-S-001 #8) — per the approved U-11 fallback, **the manual UI
  round-trip transfers to the user's environment**; this is the one open evidence item.
- **SC-3** ✅ — 403 asserted per write endpoint × 3 controllers (unprivileged caller,
  unattached-item fallback, partial-environment-write cases); UI write controls disabled
  when `!UserEditable` (component tests, both directions).
- **SC-4** ✅ — duplicate attach: behavioural exists-check → 409 (tested), composite-PK
  race escape → 409 via DbUpdateException mapping (tested); PKs verified in the model
  tests and (empirically, S-002 gate) in EF's generated DDL.
- **SC-5** ✅ — regenerated-client diff: all pre-existing generated files byte-identical;
  additions only (6 files + spec fragments + index exports + corrected generator pin).
  Full-regeneration impossibility documented in SPEC-S-006 with follow-up flagged.
- **SC-6** ✅ — characterization call-set tests pass unmodified post-integration (wiring-
  only edit, gate-verified); emission/absence/quirks covered by the component-variables
  suite; runner round-trip covered by `VariableValueComponentSerializationTests`.

## Unknowns closed
U-1..U-4 (user checkpoint), U-8 (audit parity delivered), U-9 (typed endpoints,
confirmed at IS checkpoint), U-10 (compile-level verified; no acceptance feature touches
the new paths), U-11 (answered in TOOLCHAIN-S-001; fallbacks exercised as planned).

## Open items handed to the user
1. **Manual UI round-trip** (SC-2 residue) in an environment with the full stack.
2. **R-7 ops check**: query live Properties data for collisions with the six reserved
   names/prefixes before rollout (RELEASE-NOTES.md).
3. **Deploy ordering**: dacpac before API/Monitor (RELEASE-NOTES.md).
4. Follow-up maintenance: true up generated client/spec/generator (SPEC-S-006).
5. Separate PR: tag capacity expansion (carry-over findings in REVIEW-HLPS-round1.md).
