import { HubConnection, HubConnectionBuilder } from "@microsoft/signalr";
import AppConfig from "../../app-config";

export class DeploymentHub {
  private static hubConnection: HubConnection;

  private static initializeConnection(): HubConnection {
    const baseUrl = new AppConfig().dorcApi;
    this.hubConnection = new HubConnectionBuilder()
      .withUrl(`${baseUrl}/hubs/deployments`)
      .withAutomaticReconnect()
      .build();
    
    return this.hubConnection;
  }

  static getConnection(): HubConnection {
    if (DeploymentHub.hubConnection === undefined) {
      DeploymentHub.initializeConnection();
    }
    return DeploymentHub.hubConnection;
  }
}