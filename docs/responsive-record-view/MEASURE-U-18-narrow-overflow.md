# MEASURE-U-18 — Is there a rendering defect at 375px?

| Field | Value |
|---|---|
| Status | **RESOLVED** |
| Date | 2026-08-02 |
| Governing HLPS | `docs/responsive-record-view/HLPS-responsive-record-view.md` |
| Unknown | **U-18** — blocking the IS |
| Harness | `src/dorc-web/tests/measurements/u18-narrow-overflow.test.ts` |

---

## 1. Why this measurement exists

The HLPS DRAFT asserted that 13–15 columns survive at phone width and concluded
"that is a horizontally-scrolling table". The R1 adversarial panel found the
census counted string occurrences rather than column declarations, inflating
every figure ~2.5×. Corrected, the worst view shows **6** columns, and the pilot
views show **4**.

That removed the evidence for the symptom the programme was scoped against, so
**U-18** was raised as blocking: *does a rendering defect exist at 375px at all?*
Nothing in the document established it — the claim had only ever been inferred
from a column count that was wrong.

## 2. Method

Each view's real column set was extracted from source programmatically —
count, header text, `width`, `flex-grow`, `auto-width` and
`?hidden="${this._narrowScreen}"` flags — and rendered in a `<vaadin-grid>` at a
375px container width with representative DORC content (real formats: server
names, `SEFE\` usernames, build numbers, timestamps, status values).

Measured: the grid's **internal** scrolling container
(`grid.$.table.scrollWidth` vs `.clientWidth`). Vaadin Grid scrolls internally,
so the page body does not overflow — the question is whether a user must scroll
sideways to reach a column.

Run in the project's real-browser harness (Playwright, chromium).

**Limitations, stated for the record.** Cell content is representative, not live
API data; `auto-width` columns size to content, so content length affects the
absolute numbers. Chromium only — this measures layout, not cross-engine
behaviour. Column sets are transcribed from source, not the live pages booted
with live data. The structural finding in §4 is independent of all three.

## 3. Result — as the views are written today

**7 of 9 views require horizontal scrolling at 375px.**

| View | Columns shown at ≤768px | Width required | Overflow |
|---|---|---|---|
| `page-env-history` | 4 | 764px | **+389px** |
| `page-servers-audit` | 6 | 695px | **+320px** |
| `page-databases-audit` | 6 | 684px | **+309px** |
| `env-deployments` | 5 | 639px | **+264px** |
| `page-monitor-requests` *(pilot 1)* | 4 | 610px | **+235px** |
| `env-monitor` *(pilot 4)* | 4 | 550px | **+175px** |
| `add-edit-access-control` | 5 | 498px | **+123px** |
| `component-deployment-results` *(pilot 3)* | 3 | 375px | fits |
| `page-environments-list` | 2 | 375px | fits |

**U-18 resolves YES — the defect is real.** Both pilot grid views that carry the
on-call journey's "find the failing request" step overflow, by 235px and 175px
respectively. An engineer triaging on a phone today must scroll sideways to reach
the Status column.

Note the ranking bears no relation to column count: `page-env-history` shows
**4** columns and is the worst view measured; `add-edit-access-control` shows
**5** and is the mildest overflow; `page-environments-list` shows **2** and fits.

## 4. What actually causes it

The measurement was repeated with width policy relaxed — fixed `width` values
removed, `auto-width` off, `flex-grow="0"` relaxed — **leaving column count
unchanged**:

| View | Columns | Required, width policy relaxed |
|---|---|---|
| `page-servers-audit` / `page-databases-audit` | 6 | 600px |
| `env-deployments` / `add-edit-access-control` | 5 | 500px |
| `page-env-history` / `page-monitor-requests` / `env-monitor` | 4 | 400px |
| `component-deployment-results` | 3 | **375px — fits** |
| `page-environments-list` | 2 | **375px — fits** |

The result converges to **exactly 100px per column** — Vaadin Grid's default
minimum column width. Two findings follow, and they point in opposite directions:

**(a) Width policy is a real and separable contributor.** `page-env-history`
needs 764px as written but 400px relaxed: **364px of its overflow — 94% of the
excess — comes from three hardcoded width attributes** (`170px` + `270px` +
`14em`), not from having four columns. The same pattern drives
`page-servers-audit`, where four columns carry `auto-width` with
`flex-grow="0"`, sizing to content with no ability to shrink.

**(b) But tuning width policy is not sufficient.** Even with a perfect width
policy, the floor is 100px per column. **At 375px, Vaadin Grid fits at most 3
columns.** Every view needing a 4th overflows regardless of how its widths are
declared.

## 5. Consequences for U-17

This measurement was commissioned to inform the direction decision. It does
three things to it.

**It restores the justification for doing the work at all.** The defect is real
and it lands squarely on the on-call journey. The HLPS no longer rests on an
inferred symptom.

**It refutes the cheapest option.** "Just fix the width attributes" is a genuine
third option that the corrected census made attractive, and §4(a) shows it would
materially help several views. §4(b) refutes it as a complete answer: it cannot
get `page-monitor-requests` below four columns, and four columns cannot fit. It
remains worth doing as independent cleanup — it is a handful of attribute edits
for a large gain on `page-env-history` — but it does not resolve the problem.

**It gives the design a hard numeric target, which no option had before:**

> **A view must present at most 3 columns at 375px, or it overflows.**

That target is what the direction decision should now be judged against:

- **§9.5 row-details disclosure** — collapsing to 1–2 columns plus a chevron
  lands at or under 3 by construction. It addresses the measured mechanism
  directly, and `page-projects-audit.ts:198-233` already runs this pattern in
  production over a lazy `dataProvider` with header filters.
- **The descriptor model** — also reaches ≤3 columns, via priority ranking, and
  additionally makes the priority declarative and reviewable, which §9.5 does
  not. It is a substantially larger build for the same overflow outcome.

The measurement does not choose between them: both clear the target. What it
establishes is that the choice is now about **whether declarative, reviewable
field priority is worth the extra build** — the §1.1 structural argument — and
no longer about whether a rendering defect exists. It does.

## 6. Reproducing

```
cd src/dorc-web
npx vitest run tests/measurements/u18-narrow-overflow.test.ts
```

The harness prints both tables (`U-18 RESULT` and `U-18b: WIDTH POLICY RELAXED`).

*Container note: this environment ships Playwright chromium build 1194 while the
project pins 1234. The measurement was run against the shipped build; the
project's own `vitest.config.ts` is unmodified.*
