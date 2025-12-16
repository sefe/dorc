import { HubConnection, HubConnectionBuilder } from "@microsoft/signalr";
import AppConfig, { appConfig } from "../../app-config";
import { OAUTH_SCHEME, oauthServiceContainer } from "../../services/Account/OAuthService";

export class DeploymentHub {
  private static hubConnection: HubConnection;

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
    return DeploymentHub.hubConnection;
  }
}