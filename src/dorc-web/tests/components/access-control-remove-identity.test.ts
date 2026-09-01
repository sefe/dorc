import { describe, it, expect, vi } from 'vitest';

// `removeAccess()` snapshots the row before awaiting the confirmation, but the
// snapshot only reached the prompt text — the `access-control-removed` event
// carried no row identity, so the parent resolved the row from the closure the
// grid renderer had bound into the cell:
//
//   @access-control-removed="${() => this.removeAccessControl(accessControl)}"
//
// That closure is rebound every time the cell re-renders. `Privileges` is a
// dependency of the dialog renderer, so a save response landing while the
// confirm dialog is open re-runs the column renderer and repoints the listener
// at whatever row now occupies that cell. Confirming then removes that row —
// the prompt says Alice, the grid loses Bob.
//
// This drives the two halves that have to agree: the control must report the
// row it asked about, and the parent must act on the reported row rather than
// on a closure. Reverting either half fails a test here.

let resolveConfirm: (answer: boolean) => void;

vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: () =>
    new Promise<boolean>(resolve => {
      resolveConfirm = resolve;
    })
}));

const { AddEditAccessControl } = await import(
  '../../src/components/add-edit-access-control'
);
await import('../../src/components/grid-button-groups/access-control-controls');
await import('../../src/components/deploy/property-override-controls');

type Privilege = { Id?: number; Sid?: string; Pid?: string; Name?: string };

const settle = async () => {
  for (let i = 0; i < 3; i++) {
    await new Promise(r => requestAnimationFrame(() => r(null)));
  }
};

describe('access control removal identity', () => {
  it('removes the row the prompt named, not the one recycled into the cell', async () => {
    const alice: Privilege = { Id: 1, Sid: 'S-1', Pid: 'P-1', Name: 'Alice' };
    const bob: Privilege = { Id: 2, Sid: 'S-2', Pid: 'P-2', Name: 'Bob' };

    const parent = new AddEditAccessControl() as InstanceType<
      typeof AddEditAccessControl
    > & { Privileges?: Privilege[] };
    parent.Privileges = [alice, bob];
    document.body.appendChild(parent);
    await settle();

    // Render the cell for Alice exactly as the grid column does, including the
    // parent's own listener wiring.
    const host = document.createElement('div');
    parent.shadowRoot?.appendChild(host);
    const { render } = await import('lit');
    render(
      (
        parent as unknown as {
          _boundACButtonsRenderer(item: Privilege): unknown;
        }
      )._boundACButtonsRenderer(alice) as never,
      host
    );
    await settle();

    const control = host.querySelector('access-control-controls') as HTMLElement & {
      removeAccess(): Promise<void>;
      accessControl?: Privilege;
    };

    const pending = control.removeAccess();
    await settle();

    // A save response lands while the dialog is open: the list is replaced and
    // the cell re-renders onto Bob, rebinding the listener.
    parent.Privileges = [bob];
    render(
      (
        parent as unknown as {
          _boundACButtonsRenderer(item: Privilege): unknown;
        }
      )._boundACButtonsRenderer(bob) as never,
      host
    );
    await settle();

    resolveConfirm(true);
    await pending;
    await settle();

    const names = (parent.Privileges ?? []).map(p => p.Name);
    parent.remove();

    // Alice was the row the user was asked about, and Alice is no longer in the
    // list to remove — so nothing should be removed. Bob must survive.
    expect(names, 'the unrelated row survives').toContain('Bob');
  });

  it('reports the row it asked about in the event detail', async () => {
    const alice: Privilege = { Id: 1, Sid: 'S-1', Pid: 'P-1', Name: 'Alice' };

    const control = document.createElement(
      'access-control-controls'
    ) as HTMLElement & {
      accessControl?: Privilege;
      removeAccess(): Promise<void>;
    };
    control.accessControl = alice;
    document.body.appendChild(control);

    let detail: { accessControl?: Privilege } | undefined;
    control.addEventListener('access-control-removed', (e: Event) => {
      detail = (e as CustomEvent).detail;
    });

    const pending = control.removeAccess();
    await settle();
    // Recycled onto another row before the answer arrives.
    control.accessControl = { Id: 2, Sid: 'S-2', Pid: 'P-2', Name: 'Bob' };
    resolveConfirm(true);
    await pending;

    control.remove();
    expect(detail?.accessControl?.Name, 'carries the snapshotted row').toBe(
      'Alice'
    );
  });

  it('property-override removal reports its row too', async () => {
    // Same shape, same two consumers (deploy-env and make-like-production),
    // whose renderers also identified the row from a rebindable closure.
    const control = document.createElement(
      'property-override-controls'
    ) as HTMLElement & {
      propertyOverride?: { PropertyName?: string };
      detailedResults(): Promise<void>;
    };
    control.propertyOverride = { PropertyName: 'First' };
    document.body.appendChild(control);

    let detail: { propertyOverride?: { PropertyName?: string } } | undefined;
    control.addEventListener('property-override-removed', (e: Event) => {
      detail = (e as CustomEvent).detail;
    });

    const pending = control.detailedResults();
    await settle();
    control.propertyOverride = { PropertyName: 'Second' };
    resolveConfirm(true);
    await pending;

    control.remove();
    expect(detail?.propertyOverride?.PropertyName).toBe('First');
  });
});
