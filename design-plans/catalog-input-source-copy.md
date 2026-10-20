# Present input resolution consistently

Written against: 0b44700c4a276a8c047ab6ed37ecb7e5d45076dd

## Evidence chain

- Surface: `/module-catalog` -> `deploy-from-template-dialog`, Inputs and Review steps.
- Problem: `src\dorc-web\src\components\deploy-from-template-dialog.ts:951-965` and `1394-1419` say all inputs inherit from the environment when there are no request overrides, even if module defaults apply. Review at lines 1374-1385 discards known resolution distinctions. Sensitive input copy at lines 1198-1226 can also claim an environment source without that status.
- Design evidence: The same dialog's source cards and the resolution API distinguish Environment, Default, Missing and Unset. `loadResolution` at lines 757-785 obtains names/statuses without resolved secrets.
- Owner: `DeployFromTemplateDialog` opened by `PageModuleCatalog.openDeploy`; its `bodyRenderer` renders the rail, inputs and review.
- Scope and affected surfaces: Rail summary, footer summary, parameter badge/source, non-override review rows and the reset-override action.
- Uncertainty: No rendered visual assessment; the contradiction is in runtime-selected text.

## Design decision

Present the authoritative resolution status consistently. Sensitivity modifies disclosure, never the claimed source. Unchecked, loading and failed resolution must not imply successful environment resolution. A new durable component/environment input source, if implemented in the associated feature work, must be represented distinctly and consistently too.

## Reuse

- Reuse `TerraformParameterResolutionStatus`, `statusOf`, `resolutionLoading`, `resolutionError` and existing override state.
- Preserve the existing `badge-*`, `param-source`, `footer-status`, `rail`, summary and table compositions.
- Exemplar: Existing default and environment explanations in `paramCard`.
- A private presentation function in the current dialog can share source wording across these consumers; no global design primitive is justified.

## Changes

1. `src\dorc-web\src\components\deploy-from-template-dialog.ts`
   - Change: Derive source wording from actual status, never from sensitivity or the existence of a manifest default alone.
   - Change: Show Environment, Module default, Not set, Missing, Checking or Not checked as applicable; incorporate a saved-configuration source if the new product contract adds one.
   - Change: Summaries distinguish overrides from resolved sources rather than asserting all non-overridden inputs come from the environment.
   - Change: Review rows retain source distinctions. Sensitive values/defaults remain hidden.
   - Change: Reset-override action copy must not promise an environment value when the fallback is a module default, unset or unchecked.
   - Preserve: Scalar validation, request-only overrides, omission versus false/empty, resolution cancellation/stale-response guards, and explicit plan submission.
2. `src\dorc-web\tests\components\terraform-catalog-workflow.test.ts`
   - Add focused mixed-source, sensitive-default and unavailable-resolution regressions through the public wizard behavior.
   - Assert that progress/review does not claim environment resolution for default/unset/unchecked states and that secret defaults are not disclosed.

## Scope

- Inherit: Input-source presentation within the existing wizard.
- Verify: Back/continue, target changes, resetting an override, missing required inputs and failed resolution.
- Exclude: Fetching inherited secret values, changing validation policy, or treating an unchecked source as verified readiness.

## Validation

- Product: The same input has the same source meaning in Inputs and Review; summaries remain truthful.
- Interface: Mixed sources, no overrides, some overrides, loading/error and sensitive inputs.
- System: Reuse existing source statuses and presentation owners.
- Repository: From `src\dorc-web`, `npm test -- tests/components/terraform-catalog-workflow.test.ts`, `npm run build`, and scoped `npx prettier <changed TypeScript files> --check` must pass using existing browser projects.

## Stop conditions

- If the API cannot distinguish a source, label it unchecked rather than inferring success.
- Do not expose inherited values or sensitive defaults to improve the wording.

## Design documentation

- Update the input-source explanation in `docs\Terraform\MODULES.md` only if associated saved-configuration behavior adds a new source; avoid documenting presentation internals.
