import { HubConnection, HubConnectionBuilder } from "@microsoft/signalr";
import AppConfig, { appConfig } from "../../app-config";
import { OAUTH_SCHEME, oauthServiceContainer } from "../../services/Account/OAuthService";

export class DeploymentHub {
  private static hubConnection: HubConnection;
  private static activePageCount = 0;
  private static isIntentionalDisconnect = false;
  private static handlersRegistered = false; // Track if we've already registered global handlers

  private static initializeConnection(): HubConnection {
    const baseUrl = new AppConfig().dorcApi;
    const url = `${baseUrl}/hubs/deployments`;

    // If using OAuth scheme, attach the bearer token
    if (appConfig.authenticationScheme === OAUTH_SCHEME) {
      this.hubConnection = new HubConnectionBuilder()
        .withUrl(url, {
          accessTokenFactory: () => {
            const user = oauthServiceContainer.service.signedInUser;
            if (user) return user.access_token;
            // Fallback: attempt to load user asynchronously
            return new Promise<string>(resolve => {
              oauthServiceContainer.service.getUser().subscribe(u => {
                resolve(u?.access_token ?? "");
              });
            });
          }
        })
        .withAutomaticReconnect()
        .build();
    } else {
      this.hubConnection = new HubConnectionBuilder()
        .withUrl(url)
        .withAutomaticReconnect()
        .build();
    }
    
    return this.hubConnection;
  }

  static getConnection(): HubConnection {
    if (DeploymentHub.hubConnection === undefined) {
      DeploymentHub.initializeConnection();
    }
    // Track that a page is using this connection
    DeploymentHub.activePageCount++;
    DeploymentHub.isIntentionalDisconnect = false;
    return DeploymentHub.hubConnection;
  }

  static releaseConnection(): void {
    DeploymentHub.activePageCount--;
    // Only stop the connection when no pages are using it
    if (DeploymentHub.activePageCount <= 0) {
      DeploymentHub.activePageCount = 0;
      DeploymentHub.isIntentionalDisconnect = true;
      if (DeploymentHub.hubConnection) {
        DeploymentHub.hubConnection.stop().catch(() => {
          // Silently ignore errors during intentional disconnect
        });
        // Reset handlers flag so they can be re-registered on next connection
        DeploymentHub.handlersRegistered = false;
      }
    }
  }

  static isExpectedDisconnect(): boolean {
    return DeploymentHub.isIntentionalDisconnect;
  }

  static areHandlersRegistered(): boolean {
    return DeploymentHub.handlersRegistered;
  }

  static markHandlersRegistered(): void {
    DeploymentHub.handlersRegistered = true;
  }
}