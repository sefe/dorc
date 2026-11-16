# SPEC S-007 — UI: three component tabs

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | APPROVED (executed under auto-pilot) |
| **IS step**| S-007 (IS v2, APPROVED)              |
| **Date**   | 2026-07-17                           |

## Shape delivered
Per type (pattern-setter: Containers; Cloud/APIs are scripted replicas with per-type
fields/columns):
- `environment-tabs/env-<type>.ts` — extends `PageEnvBase`; **self-fetches on activation**
  via `ByEnvId` (HLPS §5.4: automatic load, reads not editability-gated); inline
  vaadin-grid with per-type sortable columns and per-row Edit/Detach actions; Attach and
  New buttons; all write controls disabled when `!environment.UserEditable`; local
  refresh after every mutation.
- `attach-<type>.ts` — combo over `GetAll`, attach via the typed endpoint, bubbles
  `<type>-attached`.
- `add-edit-<type>.ts` — create/edit form with required-field validation and per-field
  `maxlength` matching the schema lengths (no shared constant — the tag-PR carry-over
  lesson), POST/PUT via typed client, bubbles `<type>-saved`.

**Deliberate divergence from `env-servers`** (recorded): the attached-items grid is
inline in the tab rather than a separate `attached-<type>s` component — it has exactly
one consumer, and the servers version's extra indirection exists for reuse this feature
does not have (cohesion over mirroring).

## Verification
- `npm run build` clean; 15 new component tests (per-tab columns, attach/create controls,
  read-only gating both ways, form binding, Create/Save labelling, attach dialog shape) —
  133/133 total on the chromium project.
- SC-1 grep gate: zero occurrences of the placeholder text remain in the repo.
- SC-2 evidence mode per TOOLCHAIN-S-001 #8: no full stack here — the manual UI
  round-trip transfers to the user's environment; controller tests + component tests
  gate.
