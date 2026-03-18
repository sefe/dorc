import { expect, fixture, html } from '@open-wc/testing';
import { LitElement, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { ResponsiveMixin } from '../helpers/responsive-mixin.js';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';

/**
 * Test harness component that mimics how pages use ResponsiveMixin
 * with vaadin-grid columns.
 */
@customElement('test-grid-page')
class TestGridPage extends ResponsiveMixin(LitElement) {
  @property({ type: Array })
  items = [
    { name: 'Item 1', description: 'Desc 1', details: 'Details 1' },
    { name: 'Item 2', description: 'Desc 2', details: 'Details 2' },
  ];

  static override styles = css`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
    }
    vaadin-grid {
      flex: 1;
      min-height: 0;
    }
  `;

  override render() {
    return html`
      <vaadin-grid .items="${this.items}" id="grid">
        <vaadin-grid-sort-column
          path="name"
          header="Name"
          id="col-name"
        ></vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          path="description"
          header="Description"
          id="col-description"
          ?hidden="${this._narrowScreen}"
        ></vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          path="details"
          header="Details"
          id="col-details"
          ?hidden="${this._narrowScreen}"
        ></vaadin-grid-sort-column>
      </vaadin-grid>
    `;
  }
}

describe('Grid column hiding on narrow screens', () => {
  let originalMatchMedia: typeof window.matchMedia;

  beforeEach(() => {
    originalMatchMedia = window.matchMedia;
  });

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
  });

  function mockMatchMedia(matches: boolean) {
    const listeners: Array<(e: MediaQueryListEvent) => void> = [];
    const mql: MediaQueryList = {
      matches,
      media: '(max-width: 768px)',
      onchange: null,
      addEventListener: (_event: string, handler: any) => {
        listeners.push(handler);
      },
      removeEventListener: (_event: string, handler: any) => {
        const idx = listeners.indexOf(handler);
        if (idx >= 0) listeners.splice(idx, 1);
      },
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => true,
    };
    window.matchMedia = () => mql;
    return {
      mql,
      listeners,
      triggerChange(newMatches: boolean) {
        (mql as any).matches = newMatches;
        listeners.forEach(fn =>
          fn({ matches: newMatches, media: mql.media } as MediaQueryListEvent)
        );
      },
    };
  }

  it('should show all columns on wide screens', async () => {
    mockMatchMedia(false);
    const el = await fixture<TestGridPage>(
      html`<test-grid-page></test-grid-page>`
    );
    await el.updateComplete;

    const colName = el.shadowRoot!.querySelector('#col-name') as HTMLElement;
    const colDesc = el.shadowRoot!.querySelector('#col-description') as HTMLElement;
    const colDetails = el.shadowRoot!.querySelector('#col-details') as HTMLElement;

    expect(colName.hidden).to.equal(false);
    expect(colDesc.hidden).to.equal(false);
    expect(colDetails.hidden).to.equal(false);
  });

  it('should hide secondary columns on narrow screens', async () => {
    mockMatchMedia(true);
    const el = await fixture<TestGridPage>(
      html`<test-grid-page></test-grid-page>`
    );
    await el.updateComplete;

    const colName = el.shadowRoot!.querySelector('#col-name') as HTMLElement;
    const colDesc = el.shadowRoot!.querySelector('#col-description') as HTMLElement;
    const colDetails = el.shadowRoot!.querySelector('#col-details') as HTMLElement;

    expect(colName.hidden).to.equal(false, 'Name column should always be visible');
    expect(colDesc.hidden).to.equal(true, 'Description column should be hidden on narrow');
    expect(colDetails.hidden).to.equal(true, 'Details column should be hidden on narrow');
  });

  it('should toggle columns when screen size changes', async () => {
    const mock = mockMatchMedia(false);
    const el = await fixture<TestGridPage>(
      html`<test-grid-page></test-grid-page>`
    );
    await el.updateComplete;

    const colDesc = el.shadowRoot!.querySelector('#col-description') as HTMLElement;

    // Initially visible
    expect(colDesc.hidden).to.equal(false);

    // Narrow the screen
    mock.triggerChange(true);
    await el.updateComplete;
    expect(colDesc.hidden).to.equal(true);

    // Widen the screen
    mock.triggerChange(false);
    await el.updateComplete;
    expect(colDesc.hidden).to.equal(false);
  });

  it('should keep primary column always visible', async () => {
    const mock = mockMatchMedia(false);
    const el = await fixture<TestGridPage>(
      html`<test-grid-page></test-grid-page>`
    );
    await el.updateComplete;

    const colName = el.shadowRoot!.querySelector('#col-name') as HTMLElement;

    // Wide screen
    expect(colName.hidden).to.equal(false);

    // Narrow screen
    mock.triggerChange(true);
    await el.updateComplete;
    expect(colName.hidden).to.equal(false);

    // Wide again
    mock.triggerChange(false);
    await el.updateComplete;
    expect(colName.hidden).to.equal(false);
  });

  it('should use flex layout on the host element', async () => {
    mockMatchMedia(false);
    const el = await fixture<TestGridPage>(
      html`<test-grid-page></test-grid-page>`
    );
    await el.updateComplete;

    const hostStyle = getComputedStyle(el);
    expect(hostStyle.display).to.equal('flex');
    expect(hostStyle.flexDirection).to.equal('column');
  });
});
