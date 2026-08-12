# PR 799 UI Regression Coverage

**Status:** APPROVED

## Problem

PR #799 changes routing, dialogs, notifications, theme handling, and Vaadin renderers
across most of `dorc-web`. The existing suite proves the migration rules and many
representative runtime cases, but it does not explicitly assert every material
behavior introduced or exposed by the PR.

## Scope

- Preserve the existing renderer, dependency, and confirm-prompt static gates.
- Add focused runtime tests for material gaps identified by the coverage audit.
- Add an explicit PR regression manifest so the intended coverage remains visible.
- Run the complete browser matrix and production build.

## Constraints

- Tests must be deterministic and must not require live OAuth or DEPAPP02DV.
- Production behavior must not change.
- Existing test helpers and Vitest browser conventions must be reused.
- Generated API clients must not be edited.

## Success Criteria

- Routing additionally covers hash navigation.
- Dialog and notification tests cover lifecycle, repaint, close, and action behavior.
- Renderer tests cover previously untested high-risk interactions.
- Accessibility tests cover changed menu/drawer semantics.
- A PR-specific manifest maps each major workstream to executable tests or build gates.
- Chromium, Firefox, WebKit, and `npm run build` pass.

## Unknowns Register

| Unknown | Resolution |
|---|---|
| Can live OAuth be automated from this host? | No; Microsoft interactive sign-in blocks it. Component/browser tests are the reliable automation boundary. |
| Must every changed file have a bespoke test? | No. Mechanical renderer conversions are exhaustively checked by static gates; runtime tests cover distinct behavior classes and high-risk call sites. |
| Is the scope sufficient? | Three independent audits identified the remaining material gaps; these define the implementation scope. |

## Approval

Approved under autopilot after independent dialog, renderer, and platform coverage audits.
