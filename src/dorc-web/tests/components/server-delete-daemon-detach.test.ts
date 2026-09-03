import { describe, it, expect, vi, beforeEach } from 'vitest';
import { of } from 'rxjs';

// Main added a two-stage server delete: if the server still has daemons the API
// answers `RequiresConfirmation`, and a second prompt offers to detach them and
// delete anyway. Merging that with this branch turned both prompts into awaited
// dialogs, which puts a confirmation *and* a network round trip between the row
// the user clicked and the second delete call.
//
// So the retry has to carry the snapshotted server rather than re-reading
// `this.serverDetails`, which the recycled cell may have repointed by then.

const { deleteSpy, confirmSpy } = vi.hoisted(() => ({
  deleteSpy:
    vi.fn<(args: { serverId: number; confirmed: boolean }) => unknown>(),
  confirmSpy: vi.fn<(message: string) => Promise<boolean>>(() =>
    Promise.resolve(true)
  )
}));

vi.mock('../../src/apis/dorc-api', async importOriginal => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    RefDataServersApi: class {
      refDataServersDelete = deleteSpy;
    }
  };
});

vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: (message: string) => confirmSpy(message)
}));

await import('../../src/components/grid-button-groups/server-controls');

const settle = async () => {
  for (let i = 0; i < 4; i++) {
    await new Promise(r => requestAnimationFrame(() => r(null)));
  }
};

describe('server delete with attached daemons', () => {
  beforeEach(() => {
    deleteSpy.mockReset();
    confirmSpy.mockClear();
  });

  it('retries against the server it asked about, not the recycled row', async () => {
    // First call: the server has daemons. Second: it goes through.
    deleteSpy
      .mockImplementationOnce(() =>
        of({
          Result: false,
          RequiresConfirmation: true,
          Message: 'Server has 2 daemon(s)'
        })
      )
      .mockImplementationOnce(() => of({ Result: true }));

    const el = document.createElement('server-controls') as HTMLElement & {
      serverDetails: { ServerId?: number; Name?: string };
      deleteServer(): Promise<void>;
    };
    el.serverDetails = { ServerId: 1, Name: 'PROD-01' };
    document.body.appendChild(el);

    let reported: { Name?: string } | undefined;
    el.addEventListener('server-deleted', (e: Event) => {
      reported = (e as CustomEvent).detail.server;
    });

    const pending = el.deleteServer();
    // Recycled onto another row while the first request is in flight.
    el.serverDetails = { ServerId: 2, Name: 'PROD-02' };
    await pending;
    await settle();

    el.remove();

    expect(deleteSpy).toHaveBeenNthCalledWith(1, {
      serverId: 1,
      confirmed: false
    });
    expect(
      deleteSpy,
      'the detach retry targets the same server'
    ).toHaveBeenNthCalledWith(2, { serverId: 1, confirmed: true });
    expect(reported?.Name, 'and reports it').toBe('PROD-01');

    expect(confirmSpy).toHaveBeenCalledTimes(2);
    expect(
      confirmSpy.mock.calls[1]?.[0],
      'the second prompt explains why'
    ).toContain('daemon');
  });

  it('does not delete when the detach prompt is declined', async () => {
    deleteSpy.mockImplementation(() =>
      of({
        Result: false,
        RequiresConfirmation: true,
        Message: 'Server has 2 daemon(s)'
      })
    );
    confirmSpy
      .mockImplementationOnce(() => Promise.resolve(true))
      .mockImplementationOnce(() => Promise.resolve(false));

    const el = document.createElement('server-controls') as HTMLElement & {
      serverDetails: { ServerId?: number; Name?: string };
      deleteServer(): Promise<void>;
    };
    el.serverDetails = { ServerId: 1, Name: 'PROD-01' };
    document.body.appendChild(el);

    await el.deleteServer();
    await settle();
    el.remove();

    expect(deleteSpy, 'no retry after declining').toHaveBeenCalledTimes(1);
  });
});
