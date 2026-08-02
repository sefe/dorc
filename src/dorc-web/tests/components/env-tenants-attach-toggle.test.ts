import { expect } from '../_helpers';
import '../../src/components/environment-tabs/env-tenants';

// The ATTACH control was a <paper-toggle-button> that flipped `addTenant` in a
// @click handler. It is now a <vaadin-checkbox> driven by @change reading the
// element's own `checked`. These tests pin that the observable behaviour — the
// toggle reveals and hides the inline <add-env-tenant> form, and is disabled
// for read-only or child environments — is unchanged.

interface EnvTenantsElement extends HTMLElement {
  addTenant: boolean;
  environment: unknown;
  envReadOnly: boolean;
  updateComplete: Promise<unknown>;
}

const mount = async (): Promise<EnvTenantsElement> => {
  const el = document.createElement('env-tenants') as EnvTenantsElement;
  document.body.appendChild(el);
  await el.updateComplete;
  return el;
};

const checkbox = (el: EnvTenantsElement) =>
  el.shadowRoot?.getElementById('addTenant') as
    | (HTMLElement & { checked: boolean; disabled: boolean })
    | undefined;

const attachForm = (el: EnvTenantsElement) =>
  el.shadowRoot?.querySelector('add-env-tenant');

describe('env-tenants ATTACH toggle', () => {
  let el: EnvTenantsElement;

  beforeEach(async () => {
    el = await mount();
  });

  afterEach(() => {
    el.remove();
  });

  it('renders a vaadin-checkbox, not a paper-toggle-button', () => {
    expect(checkbox(el)?.tagName.toLowerCase()).to.equal('vaadin-checkbox');
    expect(el.shadowRoot?.querySelector('paper-toggle-button')).to.equal(null);
  });

  it('starts unchecked with the inline form hidden', () => {
    expect(el.addTenant).to.equal(false);
    expect(attachForm(el)).to.equal(null);
  });

  it('reveals the inline form when checked', async () => {
    const cb = checkbox(el);
    if (!cb) throw new Error('checkbox not rendered');

    cb.checked = true;
    cb.dispatchEvent(new Event('change'));
    await el.updateComplete;

    expect(el.addTenant).to.equal(true);
    expect(attachForm(el)).to.not.equal(null);
  });

  it('hides the inline form again when unchecked', async () => {
    const cb = checkbox(el);
    if (!cb) throw new Error('checkbox not rendered');

    cb.checked = true;
    cb.dispatchEvent(new Event('change'));
    await el.updateComplete;
    cb.checked = false;
    cb.dispatchEvent(new Event('change'));
    await el.updateComplete;

    expect(el.addTenant).to.equal(false);
    expect(attachForm(el)).to.equal(null);
  });

  it('tracks the checkbox rather than blind-toggling', async () => {
    // Regression guard for the old @click handler, which flipped state
    // independently of the control and could drift out of sync with it.
    const cb = checkbox(el);
    if (!cb) throw new Error('checkbox not rendered');

    cb.checked = true;
    cb.dispatchEvent(new Event('change'));
    cb.dispatchEvent(new Event('change'));
    await el.updateComplete;

    expect(el.addTenant).to.equal(true);
  });

  it('is disabled for a read-only environment', async () => {
    el.envReadOnly = true;
    await el.updateComplete;
    expect(checkbox(el)?.disabled).to.equal(true);
  });

  it('is disabled for a child environment', async () => {
    el.environment = { ParentEnvironment: { EnvironmentName: 'PARENT' } };
    await el.updateComplete;
    expect(checkbox(el)?.disabled).to.equal(true);
  });
});
