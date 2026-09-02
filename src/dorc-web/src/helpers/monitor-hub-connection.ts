import { HubConnection } from '@microsoft/signalr';

const stopPromises = new WeakMap<HubConnection, Promise<void>>();

export function isMonitorConnectionFailure(err: unknown): boolean {
  if (typeof err !== 'object' || err === null || !('status' in err)) {
    return false;
  }

  const status = (err as { status?: unknown }).status;
  return status === 0 || status === 401;
}

export function waitForMonitorHubStop(
  connection: HubConnection
): Promise<void> | undefined {
  return stopPromises.get(connection);
}

export function stopMonitorHub(
  connection: HubConnection,
  stateChanged: (state: string) => void
): Promise<void> {
  const existingStop = stopPromises.get(connection);
  if (existingStop) {
    return existingStop;
  }

  const stopPromise = connection
    .stop()
    .catch(err => {
      console.error('Error stopping SignalR connection:', err);
    })
    .finally(() => {
      stateChanged(connection.state);
      if (stopPromises.get(connection) === stopPromise) {
        stopPromises.delete(connection);
      }
    });
  stopPromises.set(connection, stopPromise);
  return stopPromise;
}
