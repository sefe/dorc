import { expect } from '../_helpers';
import '../../src/components/attached-env-tenants';

// Regression guard for a defect that used to be live here: a grid cell
// renderer reading a reactive property did not re-run when that property
// changed, because a plain `.renderer` binding is a stable function reference
// and Grid cannot tell that its closure went stale.
//
// `attached-env-tenants` now uses columnBodyRenderer with [this.readonly] as
// its dependency, so the cell re-renders on its own. These tests assert the
// CORRECT behaviour — if one fails, a renderer has regressed to a plain
// binding or lost a dependency.

interface AttachedEnvTenants extends HTMLElement {
  readonly: boolean;
  childEnvironments: unknown;
  updateComplete: Promise<unknown>;
}

const settle = () => new Promise(r => setTimeout(r, 200));

describe('grid cell re-renders when its dependency changes', () => {
  let el: AttachedEnvTenants;

  beforeEach(async () => {
    el = document.createElement('attached-env-tenants') as AttachedEnvTenants;
    el.childEnvironments = [
      { EnvironmentId: 1, EnvironmentName: 'TENANT-A' }
    ];
    // Mirrors the real sequence: the grid is populated first, and `readonly`
    // only settles later, when the environment API call resolves.
    el.readonly = false;
    document.body.appendChild(el);
    await el.updateComplete;
    await settle();
  });

  afterEach(() => el.remove());

  const detachButton = () => {
    const grid = el.shadowRoot?.querySelector('vaadin-grid');
    const buttons = Array.from(
      grid?.querySelectorAll('vaadin-button') ?? []
    ) as (HTMLElement & { disabled: boolean })[];
    return buttons.find(b => b.getAttribute('title') === 'Detach tenant');
  };

  it('renders the detach button enabled while readonly is false', () => {
    expect(detachButton(), 'detach button not found').to.not.equal(undefined);
    expect(detachButton()?.disabled).to.equal(false);
  });

  it('disables the detach button when readonly flips to true', async () => {
    el.readonly = true;
    await el.updateComplete;
    await settle();

    // The dependency array drives this; no requestContentUpdate() call needed.
    expect(detachButton()?.disabled).to.equal(true);
  });

  it('re-enables it when readonly flips back to false', async () => {
    el.readonly = true;
    await el.updateComplete;
    await settle();
    el.readonly = false;
    await el.updateComplete;
    await settle();

    expect(detachButton()?.disabled).to.equal(false);
  });
});
