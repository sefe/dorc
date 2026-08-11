import { expect } from '../_helpers';

// Modules that render a custom element must register it themselves, rather than
// relying on some other module in the graph having done so. Three cases on this
// branch imported only the class, used solely as a type — which the compiler
// elides, leaving the element undefined.
//
// Each case gets its own import here, and nothing else imports these elements,
// so the assertion fails if the side-effect import is dropped. Putting them in a
// file that already imports the elements for other reasons would make these
// pass unconditionally.

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
