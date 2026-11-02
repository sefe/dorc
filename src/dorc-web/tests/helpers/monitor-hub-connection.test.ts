import { describe, expect, it, vi } from 'vitest';
import type { HubConnection } from '@microsoft/signalr';
import {
  monitorHubReconnectPolicy,
  stopMonitorHub,
  waitForMonitorHubStop
} from '../../src/helpers/monitor-hub-connection';

describe('monitor hub connection helpers', () => {
  it.each([
    [0, 1_000],
    [1, 2_000],
    [2, 4_000],
    [5, 30_000],
    [20, 30_000]
  ])(
    'uses a capped exponential delay after %s retries',
    (previousRetryCount, expectedDelay) => {
      expect(
        monitorHubReconnectPolicy.nextRetryDelayInMilliseconds({
          elapsedMilliseconds: 0,
          previousRetryCount,
          retryReason: new Error('connection failed')
        })
      ).toBe(expectedDelay);
    }
  );

  it('continues retrying after the default SignalR retry limit', () => {
    expect(
      monitorHubReconnectPolicy.nextRetryDelayInMilliseconds({
        elapsedMilliseconds: 120_000,
        previousRetryCount: 10,
        retryReason: new Error('connection failed')
      })
    ).toBe(30_000);
  });

  it('serializes stop calls for the same shared connection', async () => {
    let finishStop: (() => void) | undefined;
    const connection = {
      state: 'Disconnecting',
      stop: vi.fn(
        () =>
          new Promise<void>(resolve => {
            finishStop = resolve;
          })
      )
    } as unknown as HubConnection;
    const stateChanged = vi.fn();

    const firstStop = stopMonitorHub(connection, stateChanged);
    const secondStop = stopMonitorHub(connection, stateChanged);

    expect(secondStop).toBe(firstStop);
    expect(connection.stop).toHaveBeenCalledTimes(1);
    expect(waitForMonitorHubStop(connection)).toBe(firstStop);

    (connection as unknown as { state: string }).state = 'Disconnected';
    finishStop?.();
    await firstStop;

    expect(stateChanged).toHaveBeenCalledWith('Disconnected');
    expect(waitForMonitorHubStop(connection)).toBeUndefined();
  });
});
