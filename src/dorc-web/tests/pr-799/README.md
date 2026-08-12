# PR #799 UI Regression Coverage

This directory is the executable regression manifest for PR #799. Runtime
tests use rendered controls and public DOM events; they do not duplicate the
repository's source-level renderer and confirmation safety gates.

| PR workstream | Runtime coverage | Static coverage |
| --- | --- | --- |
| 1. Replace `@vaadin/router` with `universal-router` | `router-theme.test.ts` verifies query-plus-hash navigation, including a changed hash on the same route. Existing `tests/router/*.test.ts` cover matching, redirects, history, popstate, link interception, outlet rendering, and disconnect behavior. | TypeScript and the production Vite build verify all route imports and generated URLs. |
| 2. Consolidate dialogs and confirmation behaviour | `notifications-deploy.test.ts` exercises deployment confirmation and non-confirming dismissal; `dialog-reset.test.ts` proves Escape closes and immediately resets a migrated form; `request-log.test.ts` covers visible request controls and the log dialog lifecycle. | `tools/confirm-prompt-snapshot.mjs` checks direct component-state reads after `await confirmPrompt(...)`; focused runtime tests cover high-risk recycled-row actions. |
| 3. Remove Polymer dark-mode infrastructure | `router-theme.test.ts` verifies root dark state and actual Vaadin dialog-overlay Lumo inheritance. | No additional static gate: this is a browser-computed-style contract. |
| 4. Convert imperative renderers to Lit renderer directives | `project-renderers.test.ts` drives the rendered Terraform action, semantic project-component button, and real variable-lookup grid repaint; `request-log.test.ts` covers the Ace `ref` lifecycle. | `tools/renderer-audit.mjs` rejects imperative bindings and `tools/renderer-deps.mjs` checks statically resolvable render-time host dependencies. Existing renderer tests cover additional recycling and live-update cases. |
| 5. Correct notifications | `notifications-deploy.test.ts` verifies persistent successful-deploy notification content, monitor event details, and its close control. Existing `components/notification-renderer.test.ts` covers toast message repainting. | Renderer directive/dependency rules are covered by the renderer gates above. |
| 6. Remove dead patches/APIs and retain build quality | Every file here is included by `tsconfig.test.json` and runs in Chromium, Firefox, and WebKit. | `tools/gate-check.mjs`, TypeScript, ESLint, and the existing renderer/confirmation gates run through `npm run audit:renderers` / `npm run build`. |

## Focused validation

```powershell
$env:VITEST_BROWSERS = 'chromium'
npx vitest run tests/pr-799
npm run typecheck:tests
```

The manifest deliberately omits bespoke tests for mechanical renderer syntax,
dependency declarations, and post-confirmation reads because the named static
gates are exhaustive for those source-wide properties.
