/**
 * U-18 MEASUREMENT HARNESS — does a rendering defect exist at 375px?
 *
 * Not a test of production code. This reproduces each view's real column set
 * (count, header text, width / flex-grow / auto-width attributes and
 * narrow-hidden flags transcribed from source) and renders it at a 375px
 * container width with representative DORC content, to measure whether the
 * grid requires horizontal scrolling.
 *
 * Measured: the grid's internal scrolling container (vaadin-grid scrolls
 * internally, so the page body typically will not overflow — the question is
 * whether the user must scroll sideways to reach a column).
 *
 * Limitation, stated for the record: cell content is representative, not live
 * API data. auto-width columns size to content, so content length drives the
 * result. Values below are drawn from real DORC formats.
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

/** Column sets transcribed from source; content is representative DORC data. */
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
  'page-monitor-requests': [
    { header: 'Id', autoWidth: true, content: '284713' },
    { header: 'Details', autoWidth: true, content: 'Trading-Platform / PROD-EU-01 / 2026.08.02.3' },
    { header: 'Timings', autoWidth: true, hiddenNarrow: true, content: '14:32:07 → 14:39:51' },
    { header: 'User', autoWidth: true, hiddenNarrow: true, content: 'SEFE\\ben.hegarty' },
    { header: 'Status', autoWidth: true, content: 'Failed' },
    { header: '', width: '160px', content: 'Actions' },
    { header: 'Components', autoWidth: true, hiddenNarrow: true, content: 'Dorc.Api, Dorc.Web' },
  ],
  'env-monitor': [
    { header: 'Id', autoWidth: true, content: '284713' },
    { header: 'Details', autoWidth: true, content: 'Trading-Platform / PROD-EU-01 / 2026.08.02.3' },
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
              flex-grow="${c.flexGrow ?? (c.autoWidth ? '0' : '1')}"
              width="${ifDefined(c.width)}"
              resizable
            ></vaadin-grid-column>
          `
        )}
      </vaadin-grid>
    `;
  }
}

describe('U-18 — narrow-width overflow measurement at 375px', () => {
  const results: Array<{
    view: string;
    visible: number;
    required: number;
    available: number;
    overflow: number;
    overflows: boolean;
  }> = [];

  for (const [view, cols] of Object.entries(VIEWS)) {
    it(`measures ${view}`, async () => {
      const el = (await fixture(
        html`<u18-harness .cols="${cols}" .narrow="${true}"></u18-harness>`
      )) as U18Harness & { updateComplete: Promise<unknown> };
      await el.updateComplete;

      const grid = el.querySelector('#grid') as HTMLElement & {
        $: { table: HTMLElement };
      };
      // let vaadin finish auto-width calculation
      await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

      const table = grid.$.table;
      const required = table.scrollWidth;
      const available = table.clientWidth;
      const overflow = required - available;
      const visible = cols.filter(c => !c.hiddenNarrow).length;

      results.push({
        view,
        visible,
        required,
        available,
        overflow,
        overflows: overflow > 1,
      });
      expect(required).to.be.a('number');
    });
  }

  afterAll(() => {
    const rows = results
      .sort((a, b) => b.overflow - a.overflow)
      .map(
        r =>
          `${r.overflows ? 'OVERFLOW' : 'fits    '} | ${r.view.padEnd(30)} | cols=${r.visible} | needs=${r.required}px | has=${r.available}px | over=${r.overflow}px`
      )
      .join('\n');
    // eslint-disable-next-line no-console
    console.log(`\n===== U-18 RESULT (375px) =====\n${rows}\n===============================\n`);
  });
});

/**
 * Follow-up: what drives the overflow — column count, or width policy?
 * Measures each overflowing view again with fixed px/em widths removed and
 * flex-grow="0" relaxed, leaving column COUNT unchanged.
 */
describe('U-18b — is overflow caused by count or by width policy?', () => {
  const out: string[] = [];
  for (const [view, cols] of Object.entries(VIEWS)) {
    it(`re-measures ${view} with width policy relaxed`, async () => {
      const relaxed = cols.map(c => ({
        ...c,
        width: undefined,
        flexGrow: undefined,
        autoWidth: false,
      }));
      const el = (await fixture(
        html`<u18-harness .cols="${relaxed}" .narrow="${true}"></u18-harness>`
      )) as U18Harness & { updateComplete: Promise<unknown> };
      await el.updateComplete;
      const grid = el.querySelector('#grid') as HTMLElement & { $: { table: HTMLElement } };
      await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
      const t = grid.$.table;
      const over = t.scrollWidth - t.clientWidth;
      out.push(
        `${over > 1 ? 'STILL OVER' : 'now fits  '} | ${view.padEnd(30)} | cols=${relaxed.filter(c => !c.hiddenNarrow).length} | needs=${t.scrollWidth}px | over=${over}px`
      );
      expect(t.scrollWidth).to.be.a('number');
    });
  }
  afterAll(() => {
    // eslint-disable-next-line no-console
    console.log(`\n===== U-18b: WIDTH POLICY RELAXED =====\n${out.join('\n')}\n=======================================\n`);
  });
});
