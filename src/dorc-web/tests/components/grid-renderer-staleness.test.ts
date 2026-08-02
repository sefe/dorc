import { expect } from '../_helpers';
import '../../src/components/attached-env-tenants';

// Reproduction probe for AUDIT finding F-4: a grid cell renderer that reads a
// reactive property does not re-run when that property changes, because the
// renderer function reference is stable and Grid has no way to know its
// closure went stale. Nothing calls requestContentUpdate() in this component.

interface AttachedEnvTenants extends HTMLElement {
  readonly: boolean;
  childEnvironments: unknown;
  updateComplete: Promise<unknown>;
}

const settle = () => new Promise(r => setTimeout(r, 200));

describe('F-4 repro: stale grid cell after reactive property change', () => {
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

  it('DOES NOT disable the detach button when readonly flips to true', async () => {
    el.readonly = true;
    await el.updateComplete;
    await settle();

    // Documents current (incorrect) behaviour. If this ever starts failing,
    // the underlying issue has been fixed — see F-4.
    expect(detachButton()?.disabled).to.equal(false);
  });

  it('does disable it once the grid is told to re-render its content', async () => {
    el.readonly = true;
    await el.updateComplete;
    (
      el.shadowRoot?.querySelector('vaadin-grid') as unknown as {
        requestContentUpdate?: () => void;
      }
    )?.requestContentUpdate?.();
    await settle();

    // Proves the stale cell is a re-render problem, not a binding problem.
    expect(detachButton()?.disabled).to.equal(true);
  });
});
