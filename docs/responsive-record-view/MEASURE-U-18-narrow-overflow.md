# MEASURE-U-18 — Is there a rendering defect at 375px?

| Field | Value |
|---|---|
| Status | **RESOLVED — YES** (revised after R2 panel audit) |
| Date | 2026-08-02 |
| Governing HLPS | `docs/responsive-record-view/HLPS-responsive-record-view.md` |
| Unknown | **U-18** |
| Harness | `src/dorc-web/tests/measurements/u18-narrow-overflow.measure.ts` (an instrument, deliberately excluded from the CI test glob) |

> **Revision note.** The R2 panel audited the first version of this measurement
> (R2b) and confirmed its transcriptions and its central answer, but found the
> headline "hard ceiling of 3 columns" claim false (it measured Vaadin's
> *default* column width, not a floor — R2b-F1), the Details cell content
> inflated (~100px on the flagship view — R2b-F7), and the "cheapest option
> refuted" conclusion unsupported by the data as then collected (R2b-F2). This
> version corrects the content, re-runs everything, adds the missing experiment
> (U-18c), and restates the conclusions on grounds the data actually supports.

---

## 1. Why this measurement exists

The HLPS DRAFT asserted that 13–15 columns survive at phone width. The R1 panel
found the census inflated ~2.5×; corrected, the worst view shows 6 columns and
the pilot views 4. That removed the evidence for the symptom, so U-18 was
raised as blocking: *does a rendering defect exist at 375px at all?*

## 2. Method

Each view's real column set is transcribed from source — count, header text,
`width`, `flex-grow` (only where source declares it), `auto-width`,
`?hidden="${this._narrowScreen}"` — and rendered in a `<vaadin-grid>`
(`theme="compact"`, matching all nine real grids) at a 375px container with
representative DORC content. Measured: the grid's internal scrolling container
(`grid.$.table` — a private API, noted) `scrollWidth` vs `clientWidth`. Vaadin
Grid scrolls internally, so the page body never overflows; the question is
whether the user must scroll sideways to reach a column.

**Limitations (binding on interpretation):**
- Representative content, not live data. The Details cell uses the widest
  *line* of the real two-line renderer (`detailsRenderer` stacks
  "Project - Environment" over a smaller build number); the first version used
  a 44-character single line and overstated the flagship view by ~100px.
- Harness headers are plain text; real headers embed sorters and filter text
  fields that participate in `auto-width`. Real demand is therefore **higher**
  — the measured overflow is conservative.
- One content variant per column (three identical rows); the longest real row
  would measure wider. Also conservative.
- Chromium only, via an on-demand config (the committed `vitest.config.ts`
  runs three engines but never runs this file).
- "fits" rows for views hosted inside another container are
  **not-established**, not passes: `component-deployment-results` renders
  inside `<vaadin-details>` and `make-like-production` inside a
  `<vaadin-dialog>`, both at real widths below 375px.

## 3. U-18 — the views as written today

**7 of 10 measured views require horizontal scrolling at 375px.**

| View | Columns shown at ≤768px | Needs | Overflow |
|---|---|---|---|
| `page-env-history` | 4 | 764px | **+389px** |
| `page-servers-audit` | 6 | 695px | **+320px** |
| `page-databases-audit` | 6 | 684px | **+309px** |
| `env-deployments` | 5 | 639px | **+264px** |
| `page-monitor-requests` *(pilot)* | 4 | 514px | **+139px** |
| `add-edit-access-control` | 5 | 498px | **+123px** |
| `env-monitor` *(pilot)* | 4 | 454px | **+79px** |
| `component-deployment-results` *(pilot)* | 3 | 375px | fits *(not-established — hosted in `<vaadin-details>`)* |
| `make-like-production` *(pilot)* | 3 | 375px | fits *(not-established — dialog-hosted, real width < 375px)* |
| `page-environments-list` | 2 | 375px | fits |

**U-18 resolves YES.** Both journey grid views overflow; an on-call engineer
must scroll sideways to reach Status. The corrected Details content reduced
the flagship figures from the first version (+235→+139, +175→+79) without
changing the verdict — and since header-content fidelity biases the harness
low, the real figures are worse than these.

## 4. What drives it — three experiments

### 4a. Width policy is a large, separable contributor

`page-env-history` needs 764px as written; its three hardcoded widths
(170px + 270px + 14em) account for **364px of the excess (94%)**. The audit
pages' `auto-width` + `flex-grow="0"` columns size to content and refuse to
shrink. Fixing width attributes is worthwhile cleanup on those views
regardless of anything else.

### 4b. The default-width regime (NOT a floor — corrected per R2b-F1)

Re-measured with all width attributes removed, columns fall back to Vaadin's
**default** `width: '100px'` (`vaadin-grid-column-mixin.js:818-821`), giving
the arithmetically inevitable N×100: 4 columns → 400px → overflows 375px.

The first version called this "a hard ceiling of 3 columns at 375px". **That
was wrong.** 100px is a default, not a minimum — cells carry no `min-width`
and explicit smaller widths are honoured. U-18b characterises only what
happens when no width policy is set. Whether narrower explicit widths rescue
a 4-column view is an empirical question — answered by U-18c.

### 4c. The cheap fix, measured honestly (new)

`page-monitor-requests`' four surviving columns forced into 375px with
deliberately narrow explicit widths — Id 60px, Status 70px, actions 160px
(the real button group's declared width), Details flexible:

```
grid: needs=390px available=375px → still overflows (slightly)
Details column: allocated=100px, content demand=215px
→ ~54% of the identity field truncated
```

The "fit" is achieved by **truncating more than half of the Details cell** —
the field an on-call engineer identifies a request by ("Trading-Platform -
PROD-EU-01" becomes "Trading-Pla…"). The fixed 160px actions column alone
consumes 43% of the viewport. Narrow explicit widths do not solve the
problem; they convert horizontal scrolling into truncation of the
triage-critical content.

## 5. Conclusions

1. **The defect is real** (§3) and lands on the on-call journey. U-18 = YES.
2. **Fixing width attributes is worthwhile but insufficient.** It removes
   hundreds of px on the worst-authored views (§4a) — do it — but on the
   journey views the surviving columns' *content demand* (454–514px
   conservative) exceeds 375px, so any width regime that forces a fit pays in
   truncation of identity content (§4c). The first version's "3-column
   ceiling" argument is withdrawn; this truncation argument replaces it, and
   it is measured, not asserted.
3. **The design target, restated on measured grounds:** at 375px, the
   record's identity content must be given the full row width — which is a
   stacked-list presentation, not a column race. This is the §3.4 direction's
   empirical basis: a single-column list row gives the identity line
   375px − padding rather than the ~100px a four-column layout can spare it.

## 6. Reproducing

```
cd src/dorc-web
# on-demand config (not committed): include tests/measurements/*.measure.ts,
# chromium instance only — see the harness header for the exact snippet
npx vitest run --config vitest.u18.config.ts
```

Reported figures are **chromium-only**. In this container Playwright ships
build 1194 while the project pins 1234; the run used the shipped build. The
harness prints all three tables (U-18, U-18b, U-18c).
