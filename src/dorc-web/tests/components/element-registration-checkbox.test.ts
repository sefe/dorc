import { expect } from '../_helpers';
import envVariablesSource from '../../src/components/environment-tabs/env-variables.ts?raw';
import environmentsListSource from '../../src/pages/page-environments-list.ts?raw';

// The other two cases the sibling file's header used to claim: two modules that
// render <vaadin-checkbox> and had their registering import dropped, so they
// rendered an undefined element.
//
// These cannot be guarded the way add-edit-access-control is. That element is
// imported by exactly one module, so `customElements.get` before and after the
// import is a real signal. <vaadin-checkbox> is imported by seventeen, and a
// transitive dependency of both modules pulls it in — the runtime version of
// this test passes with both imports deleted, which was measured, not assumed.
//
// So this asserts the source-level fact instead: each module declares the
// import itself rather than relying on the graph. Weaker than a registration
// check, but it is the strongest thing here that can actually fail, and it
// pins the exact line that went missing.

const cases: [string, string][] = [
  ['env-variables', envVariablesSource],
  ['page-environments-list', environmentsListSource]
];

describe('modules that render vaadin-checkbox import it themselves', () => {
  for (const [name, source] of cases) {
    it(name, () => {
      expect(
        source.includes("import '@vaadin/checkbox'"),
        'declares the side-effect import rather than inheriting it'
      ).to.equal(true);
    });
  }
});
