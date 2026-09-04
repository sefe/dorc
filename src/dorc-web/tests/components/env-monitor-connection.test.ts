import { describe, expect, it, vi } from 'vitest';
import { EnvMonitor } from '../../src/components/environment-tabs/env-monitor';

const methods = EnvMonitor.prototype as unknown as {
  stopHubConnection(this: ConnectionHarness): Promise<void>;
  toggleAutoRefresh(this: ConnectionHarness): Promise<void>;
};

type ConnectionHarness = {
  autoRefresh: boolean;
  hubConnection: {
    state: string;
    start: ReturnType<typeof vi.fn>;
    stop: ReturnType<typeof vi.fn>;
  };
  hubConnectionState: string | undefined;
  refreshGrid: ReturnType<typeof vi.fn>;
  stopHubConnection(): Promise<void>;
};

function createHarness(): ConnectionHarness {
  const hubConnection = {
    state: 'Connected',
    start: vi.fn(() => Promise.resolve()),
    stop: vi.fn(() => Promise.resolve())
  };

  return {
    autoRefresh: true,
    hubConnection,
    hubConnectionState: hubConnection.state,
    refreshGrid: vi.fn(),
    stopHubConnection() {
      return methods.stopHubConnection.call(this);
    }
  };
}

describe('EnvMonitor connection handling', () => {
  it('waits for an in-flight stop before resuming', async () => {
    const harness = createHarness();
    let finishStop: (() => void) | undefined;
    harness.hubConnection.stop.mockImplementationOnce(
      () =>
        new Promise<void>(resolve => {
          harness.hubConnection.state = 'Disconnecting';
          finishStop = () => {
            harness.hubConnection.state = 'Disconnected';
            resolve();
          };
        })
    );
    harness.hubConnection.start.mockImplementationOnce(() => {
      harness.hubConnection.state = 'Connected';
      return Promise.resolve();
    });

    await methods.toggleAutoRefresh.call(harness);
    const resume = methods.toggleAutoRefresh.call(harness);

    expect(harness.hubConnection.start).not.toHaveBeenCalled();

    finishStop?.();
    await resume;

    expect(harness.hubConnection.start).toHaveBeenCalledTimes(1);
    expect(harness.refreshGrid).toHaveBeenCalledTimes(1);
    expect(harness.hubConnectionState).toBe('Connected');
  });

  it('does not reconnect after being paused again during an in-flight stop', async () => {
    const harness = createHarness();
    let finishStop: (() => void) | undefined;
    harness.hubConnection.stop.mockImplementationOnce(
      () =>
        new Promise<void>(resolve => {
          harness.hubConnection.state = 'Disconnecting';
          finishStop = () => {
            harness.hubConnection.state = 'Disconnected';
            resolve();
          };
        })
    );

    await methods.toggleAutoRefresh.call(harness);
    const staleResume = methods.toggleAutoRefresh.call(harness);
    await methods.toggleAutoRefresh.call(harness);

    finishStop?.();
    await staleResume;

    expect(harness.autoRefresh).toBe(false);
    expect(harness.hubConnection.start).not.toHaveBeenCalled();
    expect(harness.refreshGrid).not.toHaveBeenCalled();
  });
});
