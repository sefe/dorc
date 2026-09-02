import { describe, expect, it, vi } from 'vitest';
import type { HubConnection } from '@microsoft/signalr';
import {
  isMonitorConnectionFailure,
  stopMonitorHub,
  waitForMonitorHubStop
} from '../../src/helpers/monitor-hub-connection';

describe('monitor hub connection helpers', () => {
  it.each([0, 401])('classifies status %s as a connection failure', status => {
    expect(isMonitorConnectionFailure({ status })).toBe(true);
  });

  it('does not classify API validation errors as connection failures', () => {
    expect(isMonitorConnectionFailure({ status: 400 })).toBe(false);
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
