import { HubConnection, IRetryPolicy, RetryContext } from '@microsoft/signalr';

const stopPromises = new WeakMap<HubConnection, Promise<void>>();

const maximumReconnectDelayMilliseconds = 30_000;

export const monitorHubReconnectPolicy: IRetryPolicy = {
  nextRetryDelayInMilliseconds(retryContext: RetryContext): number {
    return Math.min(
      1000 * 2 ** retryContext.previousRetryCount,
      maximumReconnectDelayMilliseconds
    );
  }
};

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
