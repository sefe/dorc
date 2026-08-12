# S-001 PR 799 Regression Suite

**Status:** APPROVED

## Requirements

- Add a human-readable manifest under `src/dorc-web/tests/pr-799/`.
- The manifest must map all six PR workstreams to named tests and static gates.
- Add focused tests for:
  - router hash behavior;
  - dark theme state and overlay inheritance;
  - notification dismissal and successful-deployment action details;
  - request-control identity snapshotting;
  - log-dialog editor lifecycle;
  - deployment confirmation;
  - project action-menu accessibility;
  - representative previously untested renderer interactions and dialog resets.
- Prefer testing public events and rendered state over private implementation details.
- Do not duplicate assertions already guaranteed by renderer and confirm-prompt gates.

## Validation

- Run each browser independently to avoid the known concurrent-provider deadlock.
- Run `npm run build`.
- Review only the resulting diff.

Approved under autopilot after independent coverage audits.
