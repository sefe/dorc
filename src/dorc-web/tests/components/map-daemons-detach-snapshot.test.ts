import { describe, it, expect, vi, beforeEach } from 'vitest';
import { of } from 'rxjs';

// `detachDaemon` read `this._server` on both sides of the confirmation. Under
// `window.confirm()` that was safe — it blocked the event loop, so nothing
// could reassign the property in between. `await confirmPrompt(...)` does not
// block, and `server` is a public settable property whose setter reloads the
// mapping, so the delete could fire against a server the user was never shown:
// the prompt says "unmap from PROD-01", the request unmaps from PROD-02.
//
// The seven grid-cell controls were fixed for exactly this in round 3. This one
// was missed because that sweep was scoped to the grid-button-groups directory
// rather than to the hazard; tools/confirm-prompt-snapshot.mjs now checks every
// confirmPrompt site in src.

const { deleteSpy } = vi.hoisted(() => ({
  deleteSpy: vi.fn(() => of({}))
}));

let resolveConfirm: (answer: boolean) => void;

vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: () =>
    new Promise<boolean>(resolve => {
      resolveConfirm = resolve;
    })
}));

// map-daemons imports this class from its own module, not the barrel.
vi.mock('../../src/apis/dorc-api/apis/ServerDaemonsApi', () => ({
  ServerDaemonsApi: class {
    serverDaemonsDelete = deleteSpy;
    serverDaemonsServerIdGet = () => of([]);
  }
}));

await import('../../src/components/map-daemons');

const settle = async () => {
  for (let i = 0; i < 3; i++) {
    await new Promise(r => requestAnimationFrame(() => r(null)));
  }
};

describe('map-daemons detach', () => {
  beforeEach(() => deleteSpy.mockClear());

  it('unmaps from the server the prompt named', async () => {
    const el = document.createElement('map-daemons') as HTMLElement & {
      server: { ServerId?: number; Name?: string };
      detachDaemon(daemon: { Id?: number; Name?: string }): Promise<void>;
    };
    el.server = { ServerId: 1, Name: 'PROD-01' };
    document.body.appendChild(el);
    await settle();

    const pending = el.detachDaemon({ Id: 42, Name: 'Scheduler' });
    await settle();

    // The host reassigns the server while the confirmation is open.
    el.server = { ServerId: 2, Name: 'PROD-02' };
    await settle();

    resolveConfirm(true);
    await pending;

    el.remove();
    expect(deleteSpy).toHaveBeenCalledWith({ serverId: 1, daemonId: 42 });
  });
});
