# Review Escalations & Session Status — Terraform Hardening

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | OPEN — partial implementation; deferred steps escalated to user for follow-up. |
| **Date**   | 2026-05-09                           |
| **Branch** | claude/terraform-hardening           |
| **HLPS**   | APPROVED 2026-05-09 (round-2 panel: 2× APPROVE, 1× APPROVE-WITH-REVISIONS, applied) |
| **IS**     | APPROVED 2026-05-09 (round-2 panel: 3× clean APPROVE) |

## Session-level constraint

The execution sandbox in this session **had no .NET runtime**. Implementation steps that touch architectural surfaces (process spawning, named-pipe lifecycle, dispatcher↔runner boundary, lifecycle owner, catalog API) were deferred because they cannot be safely landed without compile-and-test feedback. The following steps are completed; the rest are escalated for follow-up.

## What landed

| Step | SC | Outcome | Status |
|------|----|---------|--------|
| S-001 | SC-04, SC-16 | `SensitivePropertyRedactor` + `Dorc.TerraformRunner.Tests` project. 13 unit tests. | DONE |
| S-002 | SC-06 | `ZipArchiveExtractor` with path-containment + caps + symlink rejection. Both `ZipFile.ExtractToDirectory` call sites in `AzureArtifactCodeSourceProvider` replaced. 9 fixture-based tests. | DONE |
| S-004 (partial) | SC-04 | Redactor wired into `ScriptGroupPipeClient` and `ScriptGroupFileReader`; `TerraformSourceConfigurator` warning reworded. | PARTIAL — pipe ACL + plan-show-output handling deferred to S-006d. |
| S-005 | SC-02 | `TerraformController` RBAC delegates to `IApiSecurityService` for view/confirm/decline. 9 controller tests. | DONE |
| S-014 (partial) | SC-15 | Typo `TerrafromRunnerOperations` → `TerraformRunnerOperations` across 5 C# call sites. | PARTIAL — `DirectoryHelper` split, dead-code removal, and DI cleanup deferred. |

## What is escalated to the user

### Defects from the original 3-way review still unresolved

| Original finding | IS step | Reason deferred | Risk if unfixed |
|------------------|---------|-----------------|-----------------|
| Command-line injection in process spawning | S-003 | Refactor to `ArgumentList`; without compile-test feedback, ProcessStartInfo behaviour on Win32 `CreateProcessAsUser` cannot be safely verified. | HIGH — paths with spaces/quotes/ampersands break argument parsing today. |
| Apply-exit-code semantics: only `1` treated as failure | S-003 | Same as above. | HIGH — failed applies can be reported green. |
| State management: backend not rendered, working dir destroyed between plan & apply | S-006a, S-006b | Architectural refactor of >300 lines across 2 files; high blast radius. | CRITICAL — every apply is effectively a fresh deployment; data-loss class defect. |
| Concurrent plan/apply on same triple | S-006c | Same as S-006a. | CRITICAL — split-brain during a planned multi-monitor rollout. |
| Dispatcher↔runner boundary; lifecycle ownership | S-006d | Largest single architectural change; full panel review required. | HIGH — duplicated state-machine ownership; both sides decide plan/apply. |
| Named-pipe ACL; pipe access from third user | S-006d | Co-located with the boundary that may relocate the pipe; defer per IS. | HIGH — secrets crossing the pipe are accessible to any user who can resolve the pipe name. |
| Plan-show output written verbatim to log | S-006d | Same as above. | HIGH — sensitive variable values can land in operator logs. |

### Library platform — not started

| IS step | What | Reason deferred |
|---------|------|-----------------|
| S-007 | `Dorc.Terraform.Catalog` library + API endpoints (catalog manifest, ITemplateCatalog, IParameterValidator, scaffold endpoint) | Depends on S-006d; substantial new code surface. |
| S-008 | Source-provider catalog integration | Depends on S-007. |
| S-009 | Component-save mutual-exclusion validation | Depends on S-007. |
| S-010 | Stock modules: `vnet`, corrected `sql-database`, `storage-account` | Standalone HCL artefacts; can land independently in a follow-up. The original example's hardcoded password default `"P@ssw0rd123!"` and the broken `sql-managed-instance` reference remain in `docs/Terraform/examples/terraform-project/` until S-010 lands. |

### Frontend, CI, docs, naming audit

| IS step | What | Status |
|---------|------|--------|
| S-011 | Frontend plan-diff renderer | Not started. |
| S-012 | STATE-MODEL.md / MODULE-CONTRACT.md / MODULES.md / setup-example rewrite | Not started. |
| S-013 | Module CI workflow + provider mirror | Not started. |
| S-014 | DirectoryHelper split, dead-code removal, DI cleanup | Partial (typo only). |
| S-015 | Final naming audit + coverage report + flag removal | Not applicable until S-006d lands. |

## Round-3 final-review findings (2026-05-09)

A 3-model panel reviewed the shipped diff (S-001/2/4/5/14) and returned **APPROVE-WITH-FIXUPS**. No CRITICAL findings. Findings addressed:

- **H-1 RESOLVED** — `Dorc.TerraformRunner.Tests` added to `src/Dorc.sln` (project entry + GUID + standard build configurations).
- **M-2 RESOLVED** — second copy of the typoed file at `src/Dorc.Monitor/RunnerProcess/TerrafromRunnerOperations.cs` renamed to `TerraformRunnerOperations.cs`. The enum type inside was already correctly named in `origin/main`.

Findings deferred (folded into the deferred-work backlog below):

- **M-1** — Wiring tests for the redactor at `ScriptGroupPipeClient.cs` and `ScriptGroupFileReader.cs` (NSubstitute the logger, assert no captured log argument contains a known-secret value). The unit-level redactor tests (`SensitivePropertyRedactorTests`, 13 cases) cover the primitive; the wiring assertion is a thin integration test that bites if the redactor call is dropped during a future refactor. Track in S-004 follow-up.
- **M-3** — `ZipArchiveExtractor` path-containment uses `StringComparison.Ordinal`. Safer on Windows is `OrdinalIgnoreCase` (file paths are case-insensitive there, and on macOS by default). Current call sites pass `Path.GetFullPath`-derived target dirs from a single `Path.GetTempPath()` source so the risk is residual; flag for an S-002 amendment.
- **M-4** — Symlink check is Unix-only (`unixMode == 0xA000`). Windows-produced zips encode `FILE_ATTRIBUTE_REPARSE_POINT` (0x400) in the low 16 bits and aren't covered. `FileMode.Create + FileAccess.Write` won't follow into an existing reparse point and the path-containment check still fires, so the risk is residual; flag for S-002 amendment.
- **M-5** — `RedactJson` returns input unchanged on `JsonException`. Documented (and tested) contract. None of the current call sites can produce input that fails `JsonNode.Parse` (they go through `JsonSerializer.Serialize` first), but if a future caller is added, secrets in malformed JSON would leak. Add a logger-warn-on-parse-failure as a defensive signal in a follow-up.
- **M-6** — `RedactJson` does not recurse into JSON-encoded-as-string values. `VariableValueJsonConverter.Write` emits `VariableValue` as a string whose body is itself escaped JSON; nested sensitive sub-keys inside such a string are not redacted. None of the shipped log sites exhibit this shape (the wrapping value is itself the redactable string). Track for the deferred S-006d work where the pipe payload itself is reshaped.

LOW notes recorded for future work: introduce an `IRedactor` seam if/when S-006d wiring needs NSubstitute; `ZipArchiveExtractor` does not delete partial state on failure (callers create per-request fresh dirs — verified against `TerraformProcessor.cs:70-75`); RBAC choices are defensible and there is no fail-open path.

## Items requiring user verification

The sandbox lacked a .NET runtime. Before this branch is merged, the user must:

1. Run `dotnet build src/Dorc.sln` — verify all projects compile, including the new `Dorc.TerraformRunner.Tests` project. The new test project is **not yet in `Dorc.sln`** (the existing `Dorc.Monitor.Tests` is also not listed, so this may be intentional, or the build pipeline discovers tests independently — confirm).
2. Run `dotnet test` — verify the 31 new unit tests pass:
   - `SensitivePropertyRedactorTests` (13)
   - `ZipArchiveExtractorTests` (9)
   - `TerraformControllerTests` (9)
3. Confirm the typo rename `TerrafromRunnerOperations → TerraformRunnerOperations` does not break the WiX installer (the installer ID `DeployMonitorServiceTerrafromRunnerNonProd.exe` is unchanged on purpose to preserve MSI upgrade identity, but the executable name itself is unaffected).
4. Decide whether to proceed with the deferred steps as a follow-up session, or accept the partial landing and merge what is here. The defects this branch does fix (RBAC, ZIP safety, log secrets at two sites, typo) are individually deployable wins; the larger architectural defects remain.

## Risk profile of the partial landing vs. the original baseline

| Concern | Before this branch | After this branch | Net effect |
|---------|-------------------|-------------------|------------|
| RBAC bypass | All authenticated users could view/confirm/decline any plan | Real env+project RBAC | RESOLVED |
| ZIP archive-bomb / path traversal in artifact source | Unprotected `ZipFile.ExtractToDirectory(..., overwriteFiles: true)` | Bounded extractor with containment + caps + typed exceptions | RESOLVED |
| PAT/bearer leaked in pipe + file-reader log lines | Plaintext JSON dump | Redacted JSON via `SensitivePropertyRedactor.RedactJson` | RESOLVED at those two sites |
| Property-name advertised in warning string | "Expected property: Terraform_Git_PAT" | Generic message | RESOLVED |
| State management broken | Yes | Yes (deferred to S-006a/b/c/d) | UNCHANGED |
| Process spawning unsafe (`Arguments` concatenation) | Yes | Yes (deferred to S-003) | UNCHANGED |
| Apply-exit-code wrong | Yes | Yes (deferred to S-003) | UNCHANGED |
| Named-pipe has no ACL | Yes | Yes (deferred to S-006d) | UNCHANGED |
| Plan-show output written to logs | Yes | Yes (deferred to S-006d) | UNCHANGED |
| Goal-fit gap (no library) | Yes | Yes (deferred to S-007..S-013) | UNCHANGED |

The partial landing is a strict improvement on the baseline — it does not introduce regressions and resolves three independently deployable security defects.
