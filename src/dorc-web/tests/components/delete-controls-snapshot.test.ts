import { describe, it, expect, vi, beforeEach } from 'vitest';
import { of } from 'rxjs';

// The delete handlers snapshot the row before awaiting the confirmation, then
// report it in the deleted-event. Both the confirm dialog and the network call
// are awaits, and these controls live in recycled grid cells on grids that
// refresh — so `this.databaseDetails` can already belong to another row by the
// time the response lands. Reading it fresh in the success callback names the
// wrong row to the parent list.
//
// Round 4 removed exactly that shadowed re-read. Nothing guarded it: putting it
// back left all 216 tests green.

const { deleteSpy } = vi.hoisted(() => ({
  deleteSpy: vi.fn(() => of({ Result: true }))
}));

vi.mock('../../src/apis/dorc-api', async importOriginal => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    RefDataDatabasesApi: class {
      refDataDatabasesDelete = deleteSpy;
    },
    RefDataServersApi: class {
      refDataServersDelete = deleteSpy;
    }
  };
});

// Auto-confirm, so the test drives the post-await path rather than the dialog.
vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: () => Promise.resolve(true)
}));

await import('../../src/components/grid-button-groups/database-controls');
await import('../../src/components/grid-button-groups/server-controls');

describe('delete controls report the row they acted on', () => {
  beforeEach(() => deleteSpy.mockClear());

  it('names the snapshotted database, not whatever recycled in', async () => {
    const el = document.createElement('database-controls') as HTMLElement & {
      databaseDetails: { Id?: number; Name?: string };
      deleteDatabase(): Promise<void>;
    };
    el.databaseDetails = { Id: 1, Name: 'Foo' };
    document.body.appendChild(el);

    let reported: { Name?: string } | undefined;
    el.addEventListener('database-deleted', (e: Event) => {
      reported = (e as CustomEvent).detail.database;
    });

    const pending = el.deleteDatabase();
    // The cell is recycled onto another row while the request is in flight.
    el.databaseDetails = { Id: 2, Name: 'Bar' };
    await pending;

    el.remove();
    expect(deleteSpy).toHaveBeenCalledWith({ databaseId: 1 });
    expect(reported?.Name, 'reports the row it deleted').toBe('Foo');
  });

  it('names the snapshotted server, not whatever recycled in', async () => {
    const el = document.createElement('server-controls') as HTMLElement & {
      serverDetails: { ServerId?: number; Name?: string };
      deleteServer(): Promise<void>;
    };
    el.serverDetails = { ServerId: 1, Name: 'Foo' };
    document.body.appendChild(el);

    let reported: { Name?: string } | undefined;
    el.addEventListener('server-deleted', (e: Event) => {
      reported = (e as CustomEvent).detail.server;
    });

    const pending = el.deleteServer();
    el.serverDetails = { ServerId: 2, Name: 'Bar' };
    await pending;

    el.remove();
    expect(deleteSpy).toHaveBeenCalledWith({ serverId: 1 });
    expect(reported?.Name, 'reports the row it deleted').toBe('Foo');
  });
});
