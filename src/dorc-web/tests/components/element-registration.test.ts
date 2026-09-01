import { expect } from '../_helpers';

// Modules that render a custom element must register it themselves, rather than
// relying on some other module in the graph having done so. Three cases on this
// branch imported only the class, used solely as a type — which the compiler
// elides, leaving the element undefined.
//
// This file covers ONE of the three, and it is the only one it can cover this
// way: `add-edit-access-control` is imported by exactly one module, so
// `customElements.get` before and after is a real signal, and the assertion
// fails if the side-effect import is dropped. The other two render
// <vaadin-checkbox>, which seventeen modules import — the same test written for
// them passes with the import deleted, because a transitive dependency supplies
// it. Those two are guarded at source level in
// element-registration-checkbox.test.ts, which says why.
//
// It needs its own file either way: `customElements` is per-iframe, so the "not
// registered beforehand" half is only true while nothing else here has loaded
// the element.

describe('modules register the elements they render', () => {
  it('env-control-center registers add-edit-access-control', async () => {
    expect(
      customElements.get('add-edit-access-control'),
      'not registered before the module loads'
    ).to.equal(undefined);

    await import('../../src/components/environment-tabs/env-control-center');

    expect(customElements.get('add-edit-access-control')).to.not.equal(
      undefined
    );
  });
});
