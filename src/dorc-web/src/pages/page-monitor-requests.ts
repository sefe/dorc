import type { Grid, GridItemModel } from '@vaadin/grid';
import {
  GridDataProviderCallback,
  GridDataProviderParams,
  GridFilterDefinition,
  GridSorterDefinition
} from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-filter';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid-sorter';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/text-field';
import { css, LitElement, PropertyValueMap, render } from 'lit';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/grid-button-groups/request-controls';
import { Notification } from '@vaadin/notification';
import {
  DeploymentRequestApiModel,
  GetRequestStatusesListResponseDto,
  PagedDataFilter,
  PagedDataSorting,
  RequestStatusesApi
} from '../apis/dorc-api';
import '@vaadin/vaadin-lumo-styles/typography.js';
import '../icons/iron-icons.js';
import '../icons/custom-icons.js';
import { ErrorNotification } from '../components/notifications/error-notification';
import { getShortLogonName } from '../helpers/user-extensions.js';
import '../components/connection-status-indicator';
import {
  DeploymentHub,
  getReceiverRegister,
  IDeploymentsEventsClient,
} from '../services/ServerEvents';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever.js';

const username = 'Username';
const status = 'Status';
const components = 'Components';
const details = 'Details';
const id = 'Id';

@customElement('page-monitor-requests')
export class PageMonitorRequests extends LitElement implements IDeploymentsEventsClient {
  @query('#grid') grid: Grid | undefined;

  // since grid is being refreshed with mupliple requests (pages) in non-deterministic way,
  // we need to store the max count of items before refresh to keep grid's cache size
  maxCountBeforeRefresh: number | undefined;

  private hubConnection: HubConnection | undefined;

  @property({ type: Boolean }) isLoading = true;

  @property({ type: Boolean }) isSearching = false;

  @property({ type: Boolean }) autoRefresh = true;

  @property({ type: String }) hubConnectionState: string | undefined = HubConnectionState.Disconnected;

  @state() noResults = false;

  // Keep reference to header root so we can manually re-render when reactive
  // properties (e.g. hubConnectionState, autoRefresh) change. Vaadin's
  // headerRenderer is only invoked when the cell is first created, so Lit's
  // normal re-render cycle does not update the header automatically.
  private _idHeaderRoot?: HTMLElement;

  userFilter: string = '';
  statusFilter: string = '';
  componentsFilter: string = '';
  idFilter: string = '';
  detailsFilter: string = '';

  static get styles() {
    return css`
      vaadin-grid {
        overflow: hidden;
        height: calc(100vh - 56px);
        --divider-color: rgb(223, 232, 239);
      }

      vaadin-text-field {
        padding: 0;
        margin: 0;
      }

      vaadin-grid-cell-content {
        padding-top: 0px;
        padding-bottom: 0px;
        margin: 0px;
      }

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

      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }

      .cover {
        object-fit: cover;
        position: fixed;
        top: 50%;
        left: 50%;
        transform: translate(-50%, -50%);
      }
    `;
  }

  render() {
    return html`
      <div id="loading" class="overlay" style="z-index: 2" ?hidden="${!this.isLoading && !this.isSearching}">
        <div class="overlay__inner">
          <div class="overlay__content">
            <span class="spinner"></span>
          </div>
        </div>
      </div>
      
      <vaadin-grid
        id="grid"
        column-reordering-allowed
        multi-sort
        .size=${200}
        theme="compact row-stripes no-row-borders no-border"
        .dataProvider=${(
          params: GridDataProviderParams<DeploymentRequestApiModel>,
          callback: GridDataProviderCallback<DeploymentRequestApiModel>
        ) => {
        if (
          params.sortOrders !== undefined &&
          params.sortOrders.length !== 1
        ) {
          return;
        }

        if (this.detailsFilter !== '' && this.detailsFilter !== undefined) {
          params.filters.push({ path: 'Project', value: this.detailsFilter });
          params.filters.push({
            path: 'EnvironmentName',
            value: this.detailsFilter
          });
          params.filters.push({
            path: 'BuildNumber',
            value: this.detailsFilter
          });
        }

        if (this.idFilter !== '' && this.idFilter !== undefined) {
          params.filters.push({ path: 'Id', value: this.idFilter });
        }

        if (this.userFilter !== '' && this.userFilter !== undefined) {
          params.filters.push({ path: 'UserName', value: this.userFilter });
        }

        if (this.statusFilter !== '' && this.statusFilter !== undefined) {
          params.filters.push({ path: 'Status', value: this.statusFilter });
        }

        if (
          this.componentsFilter !== '' &&
          this.componentsFilter !== undefined
        ) {
          params.filters.push({
            path: 'Components',
            value: this.componentsFilter
          });
        }
        const api = new RequestStatusesApi();
        api
          .requestStatusesPut({
            pagedDataOperators: {
              Filters: params.filters.map(
                (f: GridFilterDefinition): PagedDataFilter => ({
                  Path: f.path,
                  FilterValue: f.value
                })
              ),
              SortOrders: params.sortOrders.map(
                (s: GridSorterDefinition): PagedDataSorting => ({
                  Path: s.path,
                  Direction: s.direction?.toString()
                })
              )
            },
            limit: params.pageSize,
            page: params.page + 1
          })
          .subscribe({
            next: (data: GetRequestStatusesListResponseDto) => {
              data.Items?.map(
                item => (item.UserName = getShortLogonName(item.UserName))
              );
              callback(data.Items ?? [], Math.max(this.maxCountBeforeRefresh ?? 0, data.TotalItems ?? 0));

              this.dispatchEvent(
                new CustomEvent('searching-requests-finished', {
                  detail: data,
                  bubbles: true,
                  composed: true
                })
              );
            },
            error: (err: any) => {
              const errMessage = retrieveErrorMessage(err);
              const notification = new ErrorNotification();
              notification.setAttribute(
                'errorMessage',
                errMessage
              );
              this.shadowRoot?.appendChild(notification);
              notification.open();
              console.error(errMessage, err);
              callback([], 0);
              this.dispatchEvent(
                new CustomEvent('searching-requests-finished', {
                  detail: { TotalItems: 0 },
                  bubbles: true,
                  composed: true
                })
              );
            },
            complete: () => {
              this.monitorRequestsLoaded();
            }
          });
  }}
        style="z-index: 1"
      >
        <vaadin-grid-column
          path="Id"
          resizable
          auto-width
          .headerRenderer="${this.idHeaderRenderer}"
          .renderer="${this.idRenderer}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="Details"
          resizable
          auto-width
          .headerRenderer="${this.detailsHeaderRenderer}"
          .renderer="${this.detailsRenderer}"
        >
        </vaadin-grid-column>
        <vaadin-grid-column
          resizable
          .renderer="${this.timingsRenderer}"
          header="Timings"
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="User"
          .headerRenderer="${this.usersHeaderRenderer}"
          .renderer="${this.usernameRenderer}"
          resizable
          auto-width
        >
        </vaadin-grid-column>
        <vaadin-grid-column
          path="Status"
          header="Status"
          .headerRenderer="${this.statusHeaderRenderer}"
          resizable
          auto-width
        >
        </vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this._requestControlsRenderer}"
          resizable
          width="100px"
        >
        </vaadin-grid-column>
        <vaadin-grid-column
          header="Components"
          .headerRenderer="${this.componentsHeaderRenderer}"
          .renderer="${this.componentsRenderer}"
          resizable
          auto-width
        >
        </vaadin-grid-column>
      </vaadin-grid>
      <img
        class="cover"
        style="z-index: 2; height: 400px"
        ?hidden="${!this.noResults}"
        src="/hegsie_white_background_cartoon_geek_code_simple_icon_searching_12343b57-9c4e-45c6-b2f3-7765e8596718.png"
        alt="No Results Found"
      />
    `;
  }

  protected async firstUpdated(
    _changedProperties: PropertyValueMap<any> | Map<PropertyKey, unknown>
  ): Promise<void> {
    super.firstUpdated(_changedProperties);

    // Initialize SignalR connection for real-time updates
    await this.initializeSignalR();

    this.addEventListener(
      'request-cancelled',
      this.requestCancelled as EventListener
    );
    this.addEventListener(
      'request-restarted',
      this.requestRestarted as EventListener
    );
    this.addEventListener('refresh-requests', this.updateGrid as EventListener);
    this.addEventListener(
      'searching-requests-started',
      this.searchingRequestsStarted as EventListener
    );
    this.addEventListener(
      'searching-requests-finished',
      this.searchingRequestsFinished as EventListener
    );
  }

  protected updated(changed: PropertyValueMap<any>) {
    super.updated(changed);
    if (changed.has('hubConnectionState') || changed.has('autoRefresh')) {
      if (this._idHeaderRoot) {
        // Re-render header to reflect state changes
        this.idHeaderRenderer(this._idHeaderRoot);
      }
    }
  }

  disconnectedCallback(): void {
    super.disconnectedCallback();
    if (this.hubConnection) {
      this.hubConnection.stop().catch((err) => {
        console.error('Error stopping SignalR connection:', err);
      });
    }
  }

  private async initializeSignalR() {
    this.hubConnection = DeploymentHub.getConnection();

    getReceiverRegister('IDeploymentsEventsClient')
      .register(this.hubConnection, this);

    this.hubConnection.onclose(async () => {
      this.hubConnectionState = this.hubConnection?.state;
    });
    this.hubConnection.onreconnecting(() => {
      this.hubConnectionState = this.hubConnection?.state;
    });
    this.hubConnection.onreconnected(() => {
      this.hubConnectionState = this.hubConnection?.state;
    });
    
    if (this.hubConnection.state === HubConnectionState.Disconnected) {
      await this.hubConnection.start().then(() => {
        this.hubConnectionState = this.hubConnection?.state;
      }).catch((err) => {
        console.error('Error starting SignalR connection:', err);
        this.hubConnectionState = err.toString();
      });
    }
  }

  private debouncedRefreshGrid = this.debounce(() => this.refreshGrid(), 500);

  onDeploymentRequestStatusChanged(): Promise<void> {
    if (this.autoRefresh) this.debouncedRefreshGrid();
    return Promise.resolve();
  }
  onDeploymentRequestStarted(): Promise<void> {
    if (this.autoRefresh) this.debouncedRefreshGrid();
    return Promise.resolve();
  }
  onDeploymentResultStatusChanged(): Promise<void> {
    // no need to react on result change as we're covered by request status change
    return Promise.resolve();
  }

  private refreshGrid() {
    // Avoid toggling loading overlays; simply invalidate cache
    this.maxCountBeforeRefresh = 0;
    this.grid?.clearCache();
  }

  private searchingRequestsStarted(event: CustomEvent) {
    if (event.detail.value !== undefined) {
      this.debouncedInputHandler(event.detail.field, event.detail.value);
    }
  }

  private debouncedInputHandler = this.debounce(
    (field: string, value: string) => {
      switch (field) {
        case status:
          this.statusFilter = value;
          break;
        case username:
          this.userFilter = value;
          break;
        case components:
          this.componentsFilter = value;
          break;
        case id:
          this.idFilter = value;
          break;
        case details:
          this.detailsFilter = value;
          break;
        default:
          break;
      }
      this.maxCountBeforeRefresh = 0;
      this.grid?.clearCache();
      this.isSearching = true;
    },
    400 // debounce wait time
  );

  private debounce(func: (...args: any[]) => void, wait: number) {
    let timeout: number | undefined;
    return function executedFunction(...args: any[]) {
      const later = () => {
        clearTimeout(timeout);
        func(...args);
      };
      clearTimeout(timeout);
      timeout = window.setTimeout(later, wait);
    };
  }

  private searchingRequestsFinished(e: CustomEvent) {
    const data: GetRequestStatusesListResponseDto = e.detail;
    this.noResults = data.TotalItems === 0;

    this.isSearching = false;
  }

  private monitorRequestsLoaded() {
    this.isLoading = false;
  }

  updateGrid() {
    if (this.grid) {
      this.maxCountBeforeRefresh = (this.grid as any).__data?._flatSize; // there is no good way to get size of loaded items in vaadin grid(!)
      this.grid.clearCache();
      this.isLoading = true;
    }
  }

  requestCancelled(e: CustomEvent) {
    Notification.show(`Cancelled request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }

  requestRestarted(e: CustomEvent) {
    Notification.show(`Restarted request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }

  private componentsRenderer(root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>) {

    const request = model.item as DeploymentRequestApiModel;
    const elements = request.Components?.split('|');

    render(html`
      <vaadin-vertical-layout>
        ${elements?.map(
      element => html`<div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">${element}</div>`
    )}
      </vaadin-vertical-layout>
    `, root);

  }

  private usernameRenderer(root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>) {
    const request = model.item as DeploymentRequestApiModel;
    render(html`
      <div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">${request.UserName}</div>`, root);

  }

  private detailsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>
  ) => {
    const request = model.item;
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing">
          <vaadin-vertical-layout>
            <div>${request.Project} - ${request.EnvironmentName}</div>
            <div
              style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
            >
              ${request.BuildNumber}
            </div>
          </vaadin-vertical-layout>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  private timingsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>
  ) => {
    const request = model.item as DeploymentRequestApiModel;
    let sTime = '';
    let sDate = '';
    let cTime = '';
    let cDate = '';

    if (request.StartedTime !== undefined && request.StartedTime !== null) {
      sTime = new Date(request.StartedTime ?? '')?.toLocaleTimeString('en-GB');
      sDate = new Date(request.StartedTime ?? '')?.toLocaleDateString('en-GB', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric'
      });
    }
    if (request.CompletedTime !== undefined && request.CompletedTime !== null) {
      cTime = new Date(request.CompletedTime ?? '')?.toLocaleTimeString(
        'en-GB'
      );
      cDate = new Date(request.CompletedTime ?? '')?.toLocaleDateString(
        'en-GB',
        {
          day: '2-digit',
          month: '2-digit',
          year: 'numeric'
        }
      );
    }

    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing">
          <vaadin-vertical-layout
            style="line-height: var(--lumo-line-height-s);"
          >
            <div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">${`${sDate} ${sTime}`}</div>
            <div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">${`${cDate} ${cTime}`}</div>
          </vaadin-vertical-layout>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  private idRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentRequestApiModel>
  ) => {
    const request = model.item;
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing">
          <span style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"> ${request.Id} </span>
          <vaadin-button
            title="View Detailed Results"
            theme="icon small"
            @click="${() => {
          const event = new CustomEvent('open-monitor-result', {
            detail: {
              request,
              message: 'Show results for Request'
            },
            bubbles: true,
            composed: true
          });
          this.dispatchEvent(event);
        }}"
          >
            <vaadin-icon
              icon="vaadin:ellipsis-dots-h"
              style="color: cornflowerblue"
            ></vaadin-icon>
          </vaadin-button>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  _requestControlsRenderer(
    root: HTMLElement,
    _: HTMLElement,
    { item }: GridItemModel<DeploymentRequestApiModel>
  ) {
    render(
      html` <request-controls
        .requestId=${item.Id ?? 0}
        .cancelable=${!!item.UserEditable &&
        (item.Status === 'Running' ||
          item.Status === 'Requesting' ||
          item.Status === 'Pending' ||
          item.Status === 'Restarting')}
        .canRestart=${!!item.UserEditable && item.Status !== 'Pending'}
      ></request-controls>`,
      root
    );
  }

  idHeaderRenderer = (root: HTMLElement) => {
  // Store root for future manual re-renders
  this._idHeaderRoot = root;
    render(
      html`
      <vaadin-horizontal-layout style="align-items:center; gap:2px;" theme="spacing-xs">
        <connection-status-indicator
          mode="toggle"
          .state="${this.hubConnectionState}"
          .autoRefresh="${this.autoRefresh}"
          @toggle-auto-refresh="${() => {
            this.autoRefresh = !this.autoRefresh;
            if (this.autoRefresh) {
              this.refreshGrid();
            }
            this.idHeaderRenderer(root);
          }}"
        ></connection-status-indicator>

        ${!this.autoRefresh
          ? html`
          <vaadin-button
            theme="icon small"
            style="padding:0;margin:0"
            title="Manual refresh"
            @click="${() => {
              const event = new CustomEvent('refresh-requests', {
                detail: {},
                bubbles: true,
                composed: true
              });
              this.dispatchEvent(event);
            }}"
          >
            <vaadin-icon
            icon="icons:refresh"
            style="color: cornflowerblue"
            ></vaadin-icon>
          </vaadin-button>
          `
          : null}

        <vaadin-grid-sorter
          path="Id"
          direction="desc"
          style="align-items: normal"
        ></vaadin-grid-sorter>

        <vaadin-text-field
          placeholder="Id"
          clear-button-visible
          focus-target
          style="width: 100px"
          theme="small"
          @input="${(e: InputEvent) => {
          const textField = e.target as any;
          this.dispatchEvent(
            new CustomEvent('searching-requests-started', {
              detail: {
                field: id,
                value: textField?.value
              },
              bubbles: true,
              composed: true
            })
          );
        }}"
        ></vaadin-text-field>
      </vaadin-horizontal-layout>
      `,
      root
    );
  }

  detailsHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-text-field
          placeholder="Details"
          clear-button-visible
          focus-target
          style="width: 110px"
          theme="small"
          @input="${(e: InputEvent) => {
          const textField = e.target as any;
          this.dispatchEvent(
            new CustomEvent('searching-requests-started', {
              detail: {
                field: details,
                value: textField?.value
              },
              bubbles: true,
              composed: true
            })
          );
        }}"
        ></vaadin-text-field>
      `,
      root
    );
  }

  usersHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-text-field
          placeholder="Username"
          clear-button-visible
          focus-target
          style="width: 100px"
          theme="small"
          @input="${(e: InputEvent) => {
          const textField = e.target as any;

          this.dispatchEvent(
            new CustomEvent('searching-requests-started', {
              detail: {
                field: username,
                value: textField?.value
              },
              bubbles: true,
              composed: true
            })
          );
        }}"
        ></vaadin-text-field>
      `,
      root
    );
  }

  statusHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-text-field
          placeholder="Status"
          clear-button-visible
          focus-target
          style="width: 100px"
          theme="small"
          @input="${(e: InputEvent) => {
          const textField = e.target as any;

          this.dispatchEvent(
            new CustomEvent('searching-requests-started', {
              detail: {
                field: status,
                value: textField?.value
              },
              bubbles: true,
              composed: true
            })
          );
        }}"
        ></vaadin-text-field>
      `,
      root
    );
  }

  componentsHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-text-field
          placeholder="Components"
          clear-button-visible
          focus-target
          style="width: 110px"
          theme="small"
          @input="${(e: InputEvent) => {
          const textField = e.target as any;
          this.dispatchEvent(
            new CustomEvent('searching-requests-started', {
              detail: {
                field: components,
                value: textField?.value
              },
              bubbles: true,
              composed: true
            })
          );
        }}"
        ></vaadin-text-field>
      `,
      root
    );
  }
}
