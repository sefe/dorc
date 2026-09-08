# Adversarial Review — HLPS v4 Integration Amendment (delta) — v4 → v5

Panel: 2 independent reviewers on the v4 amendment only (§5.7 + §2/§3/SC-6/U-3 changes) —
(a) implementability against the resolver/DI/consumer code; (b) document consistency and
risk coverage. v3 material and user checkpoint decisions were out of review scope.

Verdict: REVISE — design confirmed implementable (constructor injection viable, all
consumers resolve via DI, both named registries are the right and only ones, runner-side
`VariableValueJsonConverter` deserialization safe for new DTOs in `Dorc.ApiModel`);
findings are wording, risk-register, and one behavioural decision. All applied in v5.

| ID | Sev | Finding (abridged) | Triage | Resolution in v5 |
|----|-----|--------------------|--------|-------------------|
| D1 | HIGH | v4 put new code + 3 injected sources on the every-deployment path (`PendingRequestProcessor`) with no risk entry — resolver defect or missed Monitor registration breaks all deployments | Accept | New R-6 with mitigations (characterization test, conditional emission, Monitor integration tests inherit `PersistentSourcesRegistry.Register`, DI-resolution gate in IS) |
| E1 | MED | §5.7 "same way servers are" implies unconditional emission (servers set `AllServers`/`EnvironmentServers` even when empty) which contradicts SC-6's empty-environment guarantee | Accept — decision: **conditional emission** | §5.7.3 now explicit: variables set only when ≥1 item attached; empty environments produce exactly the pre-change variable set |
| D5/E2 | MED | SC-6 "byte-identical" not falsifiable — resolver emits `SetPropertyValue` calls, no byte stream; `Dorc.Core.Tests` is a pure unit project | Accept (merged) | SC-6 reworded to recorded-call-set equality on the mocked `IVariableResolver`, via a characterization test written before the change |
| D3 | MED | New fixed names / `<Type>Names_` prefixes share a namespace with user-defined properties in live environments — silent collision risk uncovered | Accept | New R-7: distinctive prefixes, IS-step collision query via `IPropertiesPersistentSource`, precedence documented |
| D2 | MED | §5.1 still said "to be confirmed — U-1" after U-1 was recorded RESOLVED | Accept | §5.1 now "confirmed at checkpoint" |
| D4 | MED | U-10's "existing acceptance features unaffected" was written for CRUD-only scope; §5.7 touches the deploy path they may exercise | Accept | U-10 restated as claim-to-verify |
| E3 | LOW | Shared tag-splitting means generalizing a `private static` server-typed method; scalar-when-one/array-when-many and space→underscore quirks are part of the semantics | Accept | §5.7.2 names both quirks as test-covered requirements |
| E4 | LOW | Prefix `ApiNames_` inconsistent with entity naming | Accept | Aligned to `ApiRegistrationNames_` |
| D6 | LOW | U-9 "veto at checkpoint" ambiguous now U-1..U-4 checkpoint passed | Accept | Reworded to IS-approval checkpoint |
| D7 | LOW | R-5 read as if U-1 still open | Accept | "(resolved)" qualifier added |
| E5 | LOW | §3 mislabelled resolver tests as "integration" | Accept | Reworded to NSubstitute unit tests |

Rejected: none. Deferred: none.

Verification round (third and final for this amendment) confirmed the eleven resolutions
present; HLPS v5 is panel-approved.
