import { expect, fixture, html } from '../_helpers';
import { LitElement } from 'lit';
import { customElement } from 'lit/decorators.js';
import { NarrowListController } from '../../src/helpers/narrow-list-controller';
import { NARROW_BREAKPOINT } from '../../src/helpers/responsive-mixin';

@customElement('test-narrow-host')
class TestNarrowHost extends LitElement {
  list = new NarrowListController(this);

  override render() {
    return html`<div>${this.list.narrow ? 'narrow' : 'wide'}</div>`;
  }
}

const settle = () =>
  new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

describe('NarrowListController', () => {
  it('tracks CONTAINER width across the shared breakpoint, both directions', async () => {
    const wrapper = (await fixture(
      html`<div style="width: 900px">
        <test-narrow-host style="display:block"></test-narrow-host>
      </div>`
    )) as HTMLElement;
    const host = wrapper.querySelector('test-narrow-host') as TestNarrowHost;
    await host.updateComplete;
    await settle();
    expect(host.list.narrow).to.equal(false);

    wrapper.style.width = `${NARROW_BREAKPOINT - 100}px`;
    await settle();
    await settle();
    expect(host.list.narrow, 'collapses when the container narrows').to.equal(
      true
    );

    wrapper.style.width = '900px';
    await settle();
    await settle();
    expect(host.list.narrow, 'restores when the container widens').to.equal(
      false
    );
  });

  it('is container-driven, not viewport-driven: narrow container in a wide window', async () => {
    // window in the harness is wide; a 300px container must still collapse
    const wrapper = (await fixture(
      html`<div style="width: 300px">
        <test-narrow-host style="display:block"></test-narrow-host>
      </div>`
    )) as HTMLElement;
    const host = wrapper.querySelector('test-narrow-host') as TestNarrowHost;
    await host.updateComplete;
    await settle();
    await settle();
    expect(host.list.narrow).to.equal(true);
  });

  it('ignores zero-width measurements (hidden host does not flap the mode)', async () => {
    const wrapper = (await fixture(
      html`<div style="width: 300px">
        <test-narrow-host style="display:block"></test-narrow-host>
      </div>`
    )) as HTMLElement;
    const host = wrapper.querySelector('test-narrow-host') as TestNarrowHost;
    await settle();
    await settle();
    expect(host.list.narrow).to.equal(true);

    wrapper.style.display = 'none';
    await settle();
    await settle();
    expect(host.list.narrow, 'stays in last real mode while hidden').to.equal(
      true
    );
  });
});
