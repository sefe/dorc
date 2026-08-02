# DECISION — Idiom for new grid-bearing views (U-20)

| Field | Value |
|---|---|
| Status | **APPROVED** (blanket user approval 2026-08-02; S-008 deliverable) |
| Governing IS | `IS-responsive-record-view.md` S-008 item 5 |

## Rule

A new grid-bearing view in `dorc-web` must:

1. declare its narrow-width field priority in a **row-template module** under
   `src/row-templates/` (the §3.4 contract: primary required; metadata, chip,
   actions, details per the view);
2. use **`NarrowListController`** for narrow-mode detection — not
   `ResponsiveMixin`, not a hand-rolled `matchMedia` subscription;
3. render the §3.4 single-column list at narrow width via `dorc-list-row`
   helpers, with filters/sort/view-controls in the header-hosted bar
   (`DECISION-bar-mechanism.md`);
4. **not** use `?hidden="${this._narrowScreen}"` column bindings — this idiom
   is not accepted in review for new views.

Rationale: HLPS §1.1's root cause — responsive policy was opt-in per file, so
the worst-rendering views were the ones that never opted in. The rule closes
the gap for view #38 onward. Existing unmigrated views are untouched until the
post-pilot sequencing decision.

## Reference implementations

`page-monitor-requests` (lazy provider + filters + view controls),
`page-env-history` (declarative sort, no filters),
`component-deployment-results` (static items, hosted),
`make-like-production` (dialog-hosted).
