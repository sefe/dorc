import { HubConnection, HubConnectionBuilder } from "@microsoft/signalr";
import AppConfig from "../../app-config";

export class ServerEvents {
  private static hubConnection: HubConnection;

  private static createDeploymentHubConnection(): HubConnection {
    const baseUrl = new AppConfig().dorcApi;
    this.hubConnection = new HubConnectionBuilder()
      .withUrl(`${baseUrl}/hubs/deployments`)
      .withAutomaticReconnect()
      .build();
    
    return this.hubConnection;
  }

  static getDeploymentConnection(): HubConnection {
    if (ServerEvents.hubConnection === undefined) {
      ServerEvents.createDeploymentHubConnection();
    }
    return ServerEvents.hubConnection;
  }
}