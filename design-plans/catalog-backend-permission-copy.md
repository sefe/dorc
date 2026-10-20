# Correct catalog backend permission guidance

Written against: 0b44700c4a276a8c047ab6ed37ecb7e5d45076dd

## Evidence chain

- Surface: `/module-catalog`, expanded setup guidance.
- Problem: `src\dorc-web\src\pages\page-module-catalog.ts:880-884` says the environment service principal needs RBAC on the target subscription "only".
- Design evidence: `docs\Terraform\BACKEND-CONFIGURATION.md:18-20` requires the actual Terraform identity to have Storage Blob Data Contributor on the state container, independently of workload permissions.
- Owner: `PageModuleCatalog.render`, imported by `src\dorc-web\src\router\routes.ts:50` and routed at lines 246-252.
- Scope and affected surfaces: Catalog setup copy for Azure workload credentials and the common Azure state backend used by Azure and Aiven modules.
- Uncertainty: No browser/rendered inspection has been performed. This correction concerns directly rendered text, not visual hierarchy.

## Design decision

Describe workload authorization and backend data-plane authorization separately. Use of an environment service principal is optional; otherwise Terraform uses the runner's ambient Azure identity. Either actual Azure identity needs state-container access. An Aiven token authenticates the workload provider, not the Azure backend.

## Reuse

- Preserve existing `details.setup`, `setup-body`, headings, paragraphs and property table.
- Reuse `docs\Terraform\BACKEND-CONFIGURATION.md` as the permission contract.
- Preserve DOrc theme tokens and Vaadin controls; no new primitive or styling is needed.

## Changes

1. `src\dorc-web\src\pages\page-module-catalog.ts`
   - Change: Remove the "target subscription only" assertion. State required workload permissions plus Storage Blob Data Contributor on the separately configured state container for the actual Terraform Azure identity.
   - Change: Explain that Aiven workload credentials do not remove the Azure backend requirement.
   - Preserve: Exact property names, secure-property guidance, all-three-or-none service-principal behavior, subscription precedence and existing layout.
   - Verify: Setup guidance agrees with the backend contract without implying storage-key access or separate backend credentials are supported.
2. `src\dorc-web\tests\components\terraform-catalog-workflow.test.ts`
   - Add a focused rendered-copy regression in the existing suite if its fixture covers the catalog. Otherwise use the existing catalog suite, not a new testing framework.

## Scope

- Inherit: Only the catalog's setup guidance.
- Verify: Azure and Aiven portions are mutually consistent.
- Exclude: Authentication behavior changes, new credential probes, and a visual redesign.

## Validation

- Product: A reader understands the distinct workload and state authorization requirements.
- Interface: Expanded setup guidance remains available through the same control.
- System: No new styling or duplicated credential policy is introduced.
- Repository: From `src\dorc-web`, `npm test -- tests/components/terraform-catalog-workflow.test.ts`, `npm run build`, and scoped `npx prettier <changed TypeScript files> --check` must pass. Follow the repository's existing browser-project setup.

## Stop conditions

- Stop and reconcile this plan if the implementation introduces an explicitly separate backend identity or changes credential selection.

## Design documentation

- None; the existing backend document is already correct.
