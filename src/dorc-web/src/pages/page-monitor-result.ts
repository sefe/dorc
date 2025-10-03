import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/dialog';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-area';
import '@vaadin/text-field';

import { css, PropertyValues } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/component-deployment-results';
import '../components/grid-button-groups/request-controls';
import { Notification } from '@vaadin/notification';
import {
  DeploymentResultApiModel,
  RequestStatusesApi,
  ResultStatusesApi
} from '../apis/dorc-api';
import type { DeploymentRequestApiModel } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import '../components/request-status-card';

import { ErrorNotification } from '../components/notifications/error-notification';
import { updateMetadata } from '../helpers/html-meta-manager';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import {
  DeploymentHub,
  getReceiverRegister,
  IDeploymentsEventsClient,
  DeploymentRequestEventData,
  DeploymentResultEventData,
  getHubProxyFactory
 } from '../services/ServerEvents';

@customElement('page-monitor-result')
export class PageMonitorResult extends PageElement implements IDeploymentsEventsClient {
  @property({ type: Boolean }) loading = true;

  @property({ type: Array })
  resultItems: DeploymentResultApiModel[] | undefined;

  @property({ type: Number })
  requestId = 0;

  @property({ type: Object })
  deployRequest!: DeploymentRequestApiModel;

  @property({ type: String })
  selectedProject = '';

  @property({ type: Boolean }) resultsLoading = true;
  @property({ type: String }) hubConnectionState: string | undefined = HubConnectionState.Disconnected;
  
  private hubConnection: HubConnection | undefined;

  static get styles() {
    return css`
      .overlay {
        width: 100%;
        height: 100%;
        position: fixed;
      }

      .overlay__inner {
        width: 100%;
        height: 100%;
        position: absolute;
      }

      .overlay__content {
        left: 20%;
        position: absolute;
        top: 20%;
        transform: translate(-50%, -50%);
      }

      .spinner {
        width: 75px;
        height: 75px;
        display: inline-block;
        border-width: 2px;
        border-color: rgba(255, 255, 255, 0.05);
        border-top-color: cornflowerblue;
        animation: spin 1s infinite linear;
        border-radius: 100%;
        border-style: solid;
      }

      .small-loader {
        border: 2px solid #f3f3f3; /* Light grey */
        border-top: 2px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
      }

      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }

      paper-dialog.size-position {
        top: 16px;
        overflow: auto;
        padding: 10px;
      }

      .card-element {
        padding: 10px;
        box-shadow: 1px 2px 3px rgba(0, 0, 0, 0.2);
        height: calc(100% - 21px);
      }

      .card-element__heading {
        color: black;
      }

      .card-element__text {
        color: gray;
        margin: 2px;
      }

      .statistics-cards {
        max-width: 500px;
        display: flex;
        flex-wrap: wrap;
      }

      .statistics-cards__item {
        margin: 5px;
        flex-shrink: 0;
      }

      .requested-titles {
        width: 100px;
      }

      .vaadin-dialog-overlay {
        width: calc(100vw - (4 * var(--lumo-space-m)));
      }
    `;
  }

  protected async firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    const resultId = location.pathname.substring(
      location.pathname.lastIndexOf('/') + 1
    );
    this.requestId = Number.parseInt(decodeURIComponent(resultId), 10);

    this.refreshData();

    // Initialize SignalR connection for real-time updates scoped to this request
    await this.initializeSignalR();

    this.addEventListener(
      'refresh-monitor-result',
      this.refreshPage as EventListener
    );
  }

  disconnectedCallback(): void {
    super.disconnectedCallback();
    if (this.hubConnection && this.hubConnection.state !== HubConnectionState.Disconnected) {
      this.hubConnection.stop().catch(() => {});
    }
  }

  updated(_changedProperties: PropertyValues) {
    super.updated(_changedProperties);
    updateMetadata({
      title: `Deploy Result ${this.requestId}`,
      description: null,
      image: null,
      titleTemplate: null,
      url: undefined
    });
  }

  requestCancelled(e: CustomEvent) {
    this.refreshData();
    Notification.show(`Cancelled request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }

  requestRestarted(e: CustomEvent) {
    this.refreshData();
    Notification.show(`Restarted request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }

  private refreshData() {
    this.resultsLoading = true;
    const api = new ResultStatusesApi();
    api.resultStatusesGet({ requestId: this.requestId }).subscribe({
      next: (data: Array<DeploymentResultApiModel>) => {
        this.resultItems = data;
      },
      error: (err: any) => {
        console.error(err);
      },
      complete: () => {
        console.log('done loading result Statuses');
        this.loading = false;
        this.resultsLoading = false;
      }
    });

    this.refreshResultItems();
  }

  refreshResultItems = () => {
    this.resultsLoading = true;
    const apiRequests = new RequestStatusesApi();
    apiRequests.requestStatusesGet({ requestId: this.requestId }).subscribe({
      next: (data: DeploymentRequestApiModel) => {
        this.selectedProject = data.Project ?? '';
        this.deployRequest = data;
      },
      error: (err: any) => {
        const notification = new ErrorNotification();
        notification.setAttribute('errorMessage', err.response);
        this.shadowRoot?.appendChild(notification);
        notification.open();
        console.error(err);        
      },
      complete: () => {
        console.log('done loading request');
        this.loading = false;
        this.resultsLoading = false;
      }
    });
  }

  private async initializeSignalR() {
    if (!this.hubConnection)
      this.hubConnection = DeploymentHub.getConnection();

    getReceiverRegister('IDeploymentsEventsClient')
      .register(this.hubConnection, this);

    const hubProxy = getHubProxyFactory('IDeploymentEventsHub')
        .createHubProxy(this.hubConnection);

    this.hubConnection.onreconnected(async () => {
       await hubProxy.joinRequestGroup(this.requestId);
       this.refreshData();
       this.hubConnectionState = this.hubConnection!.state;
     });

    if (this.hubConnection.state === HubConnectionState.Disconnected) {
      try
      {
        await this.hubConnection.start();
        await hubProxy.joinRequestGroup(this.requestId);
        this.hubConnectionState = this.hubConnection.state;
      }
      catch (err)
      {
        const errorMessage = err instanceof Error ? err.message : String(err);
        this.hubConnectionState = errorMessage;
        console.error(err);
      }
    }
  }

  onDeploymentRequestStatusChanged(data: DeploymentRequestEventData): Promise<void> {
    if (data?.requestId === this.requestId) {
      const startedTime = (data.startedTime instanceof Date ? data.startedTime.toISOString() : data.startedTime);
      const completedTime = (data.completedTime instanceof Date ? data.completedTime.toISOString() : data.completedTime);

      this.deployRequest = {
        ...this.deployRequest,
        Status: data.status,
        StartedTime: startedTime ?? this.deployRequest?.StartedTime,
        CompletedTime: completedTime ?? this.deployRequest?.CompletedTime
      };
    }
    return Promise.resolve();
  }
  
  onDeploymentRequestStarted(): Promise<void> {
    return Promise.resolve();
  }

  onDeploymentResultStatusChanged(data: DeploymentResultEventData): Promise<void> {
    if (this.isEventForRequest(data, this.requestId)) {
      this.refreshResultItems();
    }
    return Promise.resolve();
  }

  isEventForRequest(event: DeploymentRequestEventData, requestId: number): boolean {
    if (!event || typeof event !== 'object') {
      return false;
    }
    const eventRequestId = Number(event.requestId);
    return eventRequestId === requestId;
  }

  render() {
    return html`
      ${this.loading
        ? html`
            <div class="overlay" style="z-index: 2">
              <div class="overlay__inner">
                <div class="overlay__content">
                  <span class="spinner"></span>
                </div>
              </div>
            </div>
          `
        : html`
            <request-status-card
              .deployRequest="${this.deployRequest}"
              .selectedProject="${this.selectedProject}"
              .hubConnectionState="${this.hubConnectionState}"
            ></request-status-card>
            ${this.resultsLoading
              ? html` <div class="small-loader"></div>`
              : html`
                  <vaadin-details
                    opened
                    summary="Deployment Component Results"
                    style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px"
                  >
                    <component-deployment-results
                      .resultItems="${this.resultItems}"
                    ></component-deployment-results>
                  </vaadin-details>
                `}
          `}
    `;
  }

  private refreshPage() {
    Notification.show(`Request ${this.deployRequest.Id} refreshing...`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });

    this.refreshData();
  }
}
