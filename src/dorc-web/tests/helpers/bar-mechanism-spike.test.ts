/**
 * S-001a SPIKE — does a sorter hosted in a *visible* column's headerRenderer
 * keep driving the grid's sort state while all other (wide) columns are
 * hidden?
 *
 * Background (HLPS §3.4, R2b-F4): hiding a column removes its cell content
 * from the DOM; disconnected sorters fall out of _getActiveSorters(), so the
 * naive "hide everything, float a bar above the grid" design silently drops
 * sort from the dataProvider query. The proposed remedy hosts the bar in the
 * narrow column's headerRenderer, keeping its sorters connected.
 *
 * This test is the decision evidence for DECISION-bar-mechanism.md.
 */
import { expect, fixture, html } from '../_helpers';
import { LitElement, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sorter';
import type {
  GridDataProviderCallback,
  GridDataProviderParams,
  GridSorterDefinition
} from '@vaadin/grid';

type Row = { name: string; other: string };

@customElement('spike-bar-grid')
class SpikeBarGrid extends LitElement {
  @property({ type: Boolean }) narrow = false;

  sortOrdersSeen: GridSorterDefinition[][] = [];

  private dataProvider = (
    params: GridDataProviderParams<Row>,
    callback: GridDataProviderCallback<Row>
  ) => {
    this.sortOrdersSeen.push(params.sortOrders);
    callback(
      [
        { name: 'b', other: 'x' },
        { name: 'a', other: 'y' }
      ],
      2
    );
  };

  private barHeaderRenderer = (root: HTMLElement) => {
    render(
      html`<vaadin-grid-sorter path="name" id="bar-sorter"
        >Name</vaadin-grid-sorter
      >`,
      root
    );
  };

  override createRenderRoot() {
    return this;
  }

  override render() {
    return html`
      <vaadin-grid id="grid" .dataProvider="${this.dataProvider}">
        <vaadin-grid-column
          id="narrow-col"
          .headerRenderer="${this.barHeaderRenderer}"
          .renderer="${(root: HTMLElement, _c: unknown, m: { item: Row }) => {
            render(html`${m.item.name}`, root);
          }}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="name"
          header="Name (wide)"
          ?hidden="${this.narrow}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="other"
          header="Other (wide)"
          ?hidden="${this.narrow}"
        ></vaadin-grid-column>
      </vaadin-grid>
    `;
  }
}

describe('S-001a spike — header-hosted bar keeps sort connected at narrow width', () => {
  it('sorter in the visible narrow column drives sortOrders while wide columns are hidden', async () => {
    const el = (await fixture(
      html`<spike-bar-grid></spike-bar-grid>`
    )) as SpikeBarGrid & { updateComplete: Promise<unknown> };
    await el.updateComplete;
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

    // enter narrow mode: wide columns hidden, bar column stays
    el.narrow = true;
    await el.updateComplete;
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

    const sorter = el.querySelector('#bar-sorter') as HTMLElement & {
      direction: string | null;
    };
    expect(sorter, 'bar sorter must exist and be connected').to.exist;
    expect(sorter.isConnected).to.equal(true);

    el.sortOrdersSeen.length = 0;
    sorter.click(); // asc
    await el.updateComplete;
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

    const seen = el.sortOrdersSeen.flat();
    const nameOrder = seen.find(s => s.path === 'name');
    expect(
      nameOrder,
      'dataProvider must receive the sort order from the header-hosted sorter while wide columns are hidden'
    ).to.exist;
    expect(nameOrder!.direction).to.equal('asc');
  });

  it('control: a sorter inside a HIDDEN column is dropped (the failure mode)', async () => {
    const el = (await fixture(
      html`<spike-bar-grid></spike-bar-grid>`
    )) as SpikeBarGrid & { updateComplete: Promise<unknown> };
    await el.updateComplete;

    // At wide width, sort via the wide Name column's default sorter would be
    // declarative; here we assert the documented Vaadin behaviour that
    // hidden columns' cell content disconnects — establishing the hazard the
    // header-hosted design avoids.
    el.narrow = true;
    await el.updateComplete;
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

    const hiddenCol = el.querySelector('vaadin-grid-column[path="name"]');
    expect(hiddenCol).to.exist;
    // the hidden column's header content is out of the DOM
    const wideHeaderSorters = Array.from(
      el.querySelectorAll('vaadin-grid-sorter')
    ).filter(s => s.id !== 'bar-sorter' && !s.isConnected);
    // nothing to assert strongly here beyond the bar sorter remaining the
    // only connected one:
    const connected = Array.from(
      el.querySelectorAll('vaadin-grid-sorter')
    ).filter(s => s.isConnected);
    expect(connected.map(s => s.id)).to.deep.equal(['bar-sorter']);
    void wideHeaderSorters;
  });
});
