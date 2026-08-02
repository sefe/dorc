/**
 * U-18 MEASUREMENT HARNESS — does a rendering defect exist at 375px?
 *
 * Not a test of production code, and deliberately named *.measure.ts so the
 * default vitest include (tests/**\/*.test.ts) never runs it in CI — it is an
 * instrument, run on demand with an explicit --config.
 *
 * It reproduces each view's real column set (count, header text, width /
 * flex-grow / auto-width attributes and narrow-hidden flags transcribed from
 * source) and renders it in a <vaadin-grid> at a 375px container width with
 * representative DORC content, measuring the grid's internal scrolling
 * container (vaadin-grid scrolls internally, so the page body never overflows
 * — the question is whether the user must scroll sideways to reach a column).
 *
 * Three experiments:
 *  U-18  — the views as written today. Overflow = defect.
 *  U-18b — width policy relaxed (fixed widths and auto-width removed, columns
 *          fall back to Vaadin's 100px DEFAULT width). NOTE (R2 panel,
 *          R2b-F1): 100px is a default, not a minimum — columns accept
 *          smaller explicit widths — so U-18b characterises the default-width
 *          regime only. It is NOT evidence of a hard per-column floor.
 *  U-18c — the "cheap fix" measured honestly: a deliberately-narrow explicit
 *          width regime that fits 375px by construction, instrumented for
 *          what it costs — truncation of the flexible identity column.
 *
 * Limitations, stated for the record:
 *  - Representative content, not live API data; auto-width sizes to content.
 *    The Details cell uses the widest LINE of the real two-line renderer
 *    (page-monitor-requests.ts detailsRenderer stacks "Project - Env" over
 *    a smaller build number), per R2b-F7 — the earlier single-line 44-char
 *    string overstated that view by ~100px.
 *  - Header cells here are plain text; real headers carry sorters and filter
 *    fields that participate in auto-width, so real demand is HIGHER —
 *    measured overflow is conservative.
 *  - grid.$.table is a private Vaadin API.
 *  - Chromium only, via an on-demand config; the committed vitest.config.ts
 *    (three engines) does not run this file.
 *  - "fits" results for views that render inside another container
 *    (component-deployment-results sits in <vaadin-details>;
 *    make-like-production sits in a <vaadin-dialog>) are not-established
 *    rather than passes: their real available width is below 375px.
 */
import { expect, fixture, html } from '../_helpers';
import { LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';

const PHONE_WIDTH = 375;

type ColSpec = {
  header: string;
  width?: string;
  flexGrow?: string;
  autoWidth?: boolean;
  hiddenNarrow?: boolean;
  content: string;
};

/**
 * Column sets transcribed from source. flexGrow is set ONLY where the source
 * declares flex-grow (R2b-F13: the earlier harness invented flex-grow="0" for
 * auto-width columns; Vaadin's default is 1 and auto-width columns rely on
 * flex-shrink: 0, so fabricating 0 was unfaithful even if immaterial).
 */
const VIEWS: Record<string, ColSpec[]> = {
  'page-servers-audit': [
    { header: 'Server', autoWidth: true, flexGrow: '0', content: 'GMPR-DORAPP02' },
    { header: 'User', autoWidth: true, flexGrow: '0', content: 'SEFE\\ben.hegarty' },
    { header: 'Action', autoWidth: true, flexGrow: '0', content: 'Update' },
    { header: 'Date', autoWidth: true, flexGrow: '0', content: '02/08/2026 14:32:07' },
    { header: 'From', flexGrow: '1', content: 'Windows Server 2019' },
    { header: 'To', flexGrow: '1', content: 'Windows Server 2022' },
  ],
  'page-databases-audit': [
    { header: 'Database', autoWidth: true, flexGrow: '0', content: 'DORC_PROD_01' },
    { header: 'User', autoWidth: true, flexGrow: '0', content: 'SEFE\\ben.hegarty' },
    { header: 'Action', autoWidth: true, flexGrow: '0', content: 'Update' },
    { header: 'Date', autoWidth: true, flexGrow: '0', content: '02/08/2026 14:32:07' },
    { header: 'From', flexGrow: '1', content: 'AlwaysOn: false' },
    { header: 'To', flexGrow: '1', content: 'AlwaysOn: true' },
  ],
  'env-deployments': [
    { header: 'Request Id', width: '110px', content: '284713' },
    { header: 'Component Name', autoWidth: true, content: 'Dorc.Api.Deploy' },
    { header: 'Request Build Number', autoWidth: true, content: '2026.08.02.3' },
    { header: 'Requested', autoWidth: true, content: '02/08/2026 14:32' },
    { header: 'Status', content: 'Complete' },
  ],
  'add-edit-access-control': [
    { header: 'Name', autoWidth: true, content: 'SEFE\\DORC-Prod-Admins' },
    { header: 'Write', autoWidth: true, content: '[x]' },
    { header: 'Read Secrets', autoWidth: true, content: '[x]' },
    { header: 'Owner', autoWidth: true, content: '[ ]' },
    { header: 'Actions', autoWidth: true, content: 'Edit Delete' },
  ],
  'page-env-history': [
    { header: 'Environment Name', content: 'PROD-EU-01' },
    { header: 'Updated Date', width: '170px', content: '02/08/2026 14:32:07' },
    { header: 'Updated By', hiddenNarrow: true, content: 'SEFE\\ben.hegarty' },
    { header: 'Update Type', hiddenNarrow: true, content: 'Component' },
    { header: 'Old Version', width: '170px', hiddenNarrow: true, content: '2026.07.28.1' },
    { header: 'New Version', width: '170px', hiddenNarrow: true, content: '2026.08.02.3' },
    { header: 'Details', hiddenNarrow: true, content: 'Deployed via pipeline' },
    { header: 'Comment', width: '270px', content: 'Routine release' },
    { header: '', width: '14em', content: 'Edit' },
  ],
  // Details content = widest line of the real stacked renderer (R2b-F7).
  'page-monitor-requests': [
    { header: 'Id', autoWidth: true, content: '284713' },
    { header: 'Details', autoWidth: true, content: 'Trading-Platform - PROD-EU-01' },
    { header: 'Timings', autoWidth: true, hiddenNarrow: true, content: '14:32:07 → 14:39:51' },
    { header: 'User', autoWidth: true, hiddenNarrow: true, content: 'SEFE\\ben.hegarty' },
    { header: 'Status', autoWidth: true, content: 'Failed' },
    { header: '', width: '160px', content: 'Actions' },
    { header: 'Components', autoWidth: true, hiddenNarrow: true, content: 'Dorc.Api, Dorc.Web' },
  ],
  'env-monitor': [
    { header: 'Id', autoWidth: true, content: '284713' },
    { header: 'Details', autoWidth: true, content: 'Trading-Platform - PROD-EU-01' },
    { header: 'Timings', autoWidth: true, hiddenNarrow: true, content: '14:32:07 → 14:39:51' },
    { header: 'User', autoWidth: true, hiddenNarrow: true, content: 'SEFE\\ben.hegarty' },
    { header: 'Status', autoWidth: true, content: 'Failed' },
    { header: '', width: '100px', content: 'Actions' },
    { header: 'Components', autoWidth: true, hiddenNarrow: true, content: 'Dorc.Api, Dorc.Web' },
  ],
  'component-deployment-results': [
    { header: 'Component Name', autoWidth: true, content: 'Dorc.Api.Deploy' },
    { header: 'Timings', autoWidth: true, hiddenNarrow: true, content: '14:32:07 → 14:39:51' },
    { header: 'Status', autoWidth: true, content: 'Failed' },
    { header: 'Actions', autoWidth: true, content: 'Log Plan' },
    { header: 'Log', autoWidth: true, hiddenNarrow: true, content: 'System.Exception: deploy step failed' },
  ],
  'page-environments-list': [
    { header: 'Name', content: 'PROD-EU-01' },
    { header: 'Owner', hiddenNarrow: true, content: 'SEFE\\ben.hegarty' },
    { header: 'Description', hiddenNarrow: true, content: 'Primary production environment' },
    { header: 'Secure', hiddenNarrow: true, content: 'Yes' },
    { header: 'Prod', hiddenNarrow: true, content: 'Yes' },
    { header: 'File Share', hiddenNarrow: true, content: '\\\\gmpr-fs01\\dorc' },
    { header: 'Notes', hiddenNarrow: true, content: 'Managed by platform team' },
    { header: '', content: 'Actions' },
  ],
  // Pilot step 5 (R2b-F15): dialog-hosted; measured here at 375px although its
  // real available width inside the dialog is smaller still — conservative.
  // Transcribed from make-like-production.ts:152-170 (corrected per R3-F2:
  // the first transcription fabricated auto-width/flex specs not in source).
  'make-like-production': [
    { header: 'Property Name', width: '300px', flexGrow: '0', content: 'DeploymentTargetDatabase' },
    { header: 'Property Value', width: '300px', flexGrow: '0', content: 'GMPR-SQL01.sefe.local' },
    { header: '', content: 'Override' },
  ],
};

@customElement('u18-harness')
class U18Harness extends LitElement {
  @property({ type: Array }) cols: ColSpec[] = [];
  @property({ type: Boolean }) narrow = true;

  override createRenderRoot() {
    return this; // light DOM keeps grid measurement simple
  }

  override render() {
    const visible = this.cols.filter(c => !(this.narrow && c.hiddenNarrow));
    const item: Record<string, string> = {};
    visible.forEach((c, i) => (item[`c${i}`] = c.content));
    return html`
      <vaadin-grid
        id="grid"
        theme="compact no-border"
        .items="${[item, item, item]}"
        style="width:${PHONE_WIDTH}px"
      >
        ${visible.map(
          (c, i) => html`
            <vaadin-grid-column
              path="c${i}"
              header="${c.header}"
              ?auto-width="${!!c.autoWidth}"
              flex-grow="${ifDefined(c.flexGrow)}"
              width="${ifDefined(c.width)}"
              resizable
            ></vaadin-grid-column>
          `
        )}
      </vaadin-grid>
    `;
  }
}

async function measure(el: U18Harness & { updateComplete: Promise<unknown> }) {
  await el.updateComplete;
  const grid = el.querySelector('#grid') as HTMLElement & { $: { table: HTMLElement } };
  await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
  const t = grid.$.table;
  return { grid, required: t.scrollWidth, available: t.clientWidth };
}

describe('U-18 — narrow-width overflow at 375px (views as written)', () => {
  const results: string[] = [];
  for (const [view, cols] of Object.entries(VIEWS)) {
    it(`measures ${view}`, async () => {
      const el = (await fixture(
        html`<u18-harness .cols="${cols}" .narrow="${true}"></u18-harness>`
      )) as U18Harness & { updateComplete: Promise<unknown> };
      const { required, available } = await measure(el);
      const over = required - available;
      results.push(
        `${over > 1 ? 'OVERFLOW' : 'fits    '} | ${view.padEnd(30)} | cols=${cols.filter(c => !c.hiddenNarrow).length} | needs=${required}px | over=${over}px`
      );
      expect(required).to.be.greaterThan(0);
    });
  }
  afterAll(() => {
    // eslint-disable-next-line no-console
    console.log(`\n===== U-18 (375px, as written) =====\n${results.join('\n')}\n`);
  });
});

describe('U-18b — default-width regime (NOT a floor; see header comment)', () => {
  const results: string[] = [];
  for (const [view, cols] of Object.entries(VIEWS)) {
    it(`re-measures ${view} at Vaadin default widths`, async () => {
      const relaxed = cols.map(c => ({ ...c, width: undefined, flexGrow: undefined, autoWidth: false }));
      const el = (await fixture(
        html`<u18-harness .cols="${relaxed}" .narrow="${true}"></u18-harness>`
      )) as U18Harness & { updateComplete: Promise<unknown> };
      const { required, available } = await measure(el);
      const over = required - available;
      results.push(
        `${over > 1 ? 'over' : 'fits'} | ${view.padEnd(30)} | cols=${relaxed.filter(c => !c.hiddenNarrow).length} | needs=${required}px`
      );
      expect(required).to.be.greaterThan(0);
    });
  }
  afterAll(() => {
    // eslint-disable-next-line no-console
    console.log(`\n===== U-18b (default 100px widths) =====\n${results.join('\n')}\n`);
  });
});

describe('U-18c — the cheap fix measured honestly (explicit narrow widths)', () => {
  it('fits 375px by construction but truncates the identity column', async () => {
    // page-monitor-requests, 4 visible columns, forced into 375px:
    // Id 60px + Status 70px + Actions 160px fixed; Details flexible.
    const cols: ColSpec[] = [
      { header: 'Id', width: '60px', flexGrow: '0', content: '284713' },
      { header: 'Details', flexGrow: '1', content: 'Trading-Platform - PROD-EU-01' },
      { header: 'Status', width: '70px', flexGrow: '0', content: 'Failed' },
      { header: '', width: '160px', flexGrow: '0', content: 'Actions' },
    ];
    const el = (await fixture(
      html`<u18-harness .cols="${cols}" .narrow="${true}"></u18-harness>`
    )) as U18Harness & { updateComplete: Promise<unknown> };
    const { grid, required, available } = await measure(el);

    // find the Details body cell content and measure truncation
    const contents = Array.from(el.querySelectorAll('vaadin-grid-cell-content'));
    const details = contents.find(c => c.textContent?.includes('Trading-Platform'))!;
    const cellWidth = details.getBoundingClientRect().width;
    // untruncated demand: measure the same text unconstrained
    const probe = document.createElement('span');
    probe.style.cssText = 'position:absolute;visibility:hidden;white-space:nowrap;font:inherit';
    probe.textContent = details.textContent;
    details.appendChild(probe);
    const demand = probe.getBoundingClientRect().width;
    probe.remove();
    const truncatedPct = Math.max(0, Math.round((1 - cellWidth / demand) * 100));

    // eslint-disable-next-line no-console
    console.log(
      `\n===== U-18c (cheap fix: explicit narrow widths) =====\n` +
        `grid: needs=${required}px available=${available}px → ${required - available > 1 ? 'OVERFLOWS' : 'fits'}\n` +
        `Details column: allocated=${Math.round(cellWidth)}px, content demand=${Math.round(demand)}px → ` +
        `~${truncatedPct}% of the identity field truncated\n`
    );
    expect(required).to.be.greaterThan(0);
    void grid;
  });
});
