import { expect, fixture, html } from '@open-wc/testing';
import { LitElement, css } from 'lit';
import { customElement } from 'lit/decorators.js';
import { ResponsiveMixin } from './responsive-mixin.js';

// Test element that uses the ResponsiveMixin
@customElement('test-responsive-element')
class TestResponsiveElement extends ResponsiveMixin(LitElement) {
  static override styles = css`
    :host { display: block; }
    .wide-only { display: block; }
  `;

  override render() {
    return html`
      <div id="always-visible">Always visible</div>
      <div id="wide-only" ?hidden="${this._narrowScreen}">Wide screen only</div>
    `;
  }
}

describe('ResponsiveMixin', () => {
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

  it('should set _narrowScreen to false on wide screens', async () => {
    mockMatchMedia(false);
    const el = await fixture<TestResponsiveElement>(
      html`<test-responsive-element></test-responsive-element>`
    );

    expect(el._narrowScreen).to.equal(false);
  });

  it('should set _narrowScreen to true on narrow screens', async () => {
    mockMatchMedia(true);
    const el = await fixture<TestResponsiveElement>(
      html`<test-responsive-element></test-responsive-element>`
    );

    expect(el._narrowScreen).to.equal(true);
  });

  it('should hide elements bound to _narrowScreen on narrow screens', async () => {
    mockMatchMedia(true);
    const el = await fixture<TestResponsiveElement>(
      html`<test-responsive-element></test-responsive-element>`
    );
    await el.updateComplete;

    const wideOnly = el.shadowRoot!.querySelector('#wide-only') as HTMLElement;
    expect(wideOnly.hidden).to.equal(true);
  });

  it('should show elements bound to _narrowScreen on wide screens', async () => {
    mockMatchMedia(false);
    const el = await fixture<TestResponsiveElement>(
      html`<test-responsive-element></test-responsive-element>`
    );
    await el.updateComplete;

    const wideOnly = el.shadowRoot!.querySelector('#wide-only') as HTMLElement;
    expect(wideOnly.hidden).to.equal(false);
  });

  it('should react to matchMedia changes dynamically', async () => {
    const mock = mockMatchMedia(false);
    const el = await fixture<TestResponsiveElement>(
      html`<test-responsive-element></test-responsive-element>`
    );
    await el.updateComplete;

    expect(el._narrowScreen).to.equal(false);

    // Simulate screen resize to narrow
    mock.triggerChange(true);
    await el.updateComplete;

    expect(el._narrowScreen).to.equal(true);
    const wideOnly = el.shadowRoot!.querySelector('#wide-only') as HTMLElement;
    expect(wideOnly.hidden).to.equal(true);

    // Simulate screen resize back to wide
    mock.triggerChange(false);
    await el.updateComplete;

    expect(el._narrowScreen).to.equal(false);
    expect(wideOnly.hidden).to.equal(false);
  });

  it('should remove media listener on disconnect', async () => {
    const mock = mockMatchMedia(false);
    const el = await fixture<TestResponsiveElement>(
      html`<test-responsive-element></test-responsive-element>`
    );

    expect(mock.listeners.length).to.equal(1);

    // Disconnect the element
    el.remove();

    expect(mock.listeners.length).to.equal(0);
  });

  it('should always show the always-visible element regardless of screen size', async () => {
    // Wide screen
    mockMatchMedia(false);
    const elWide = await fixture<TestResponsiveElement>(
      html`<test-responsive-element></test-responsive-element>`
    );
    await elWide.updateComplete;
    const alwaysWide = elWide.shadowRoot!.querySelector('#always-visible') as HTMLElement;
    expect(alwaysWide.hidden).to.equal(false);

    // Narrow screen
    mockMatchMedia(true);
    const elNarrow = await fixture<TestResponsiveElement>(
      html`<test-responsive-element></test-responsive-element>`
    );
    await elNarrow.updateComplete;
    const alwaysNarrow = elNarrow.shadowRoot!.querySelector('#always-visible') as HTMLElement;
    expect(alwaysNarrow.hidden).to.equal(false);
  });
});
