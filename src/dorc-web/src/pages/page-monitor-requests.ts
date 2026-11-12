import { columnBodyRenderer, columnHeaderRenderer } from '@vaadin/grid/lit';
import type { Grid } from '@vaadin/grid';
import '../components/dorc-spinner';
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
import { css, PropertyValueMap } from 'lit';
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
import '../icons/iron-icons.js';
import '../icons/custom-icons.js';
import { ErrorNotification } from '../components/notifications/error-notification';
import { getShortLogonName } from '../helpers/user-extensions.js';
import '../components/connection-status-indicator';
import {
  DeploymentHub,
  getReceiverRegister,
  IDeploymentsEventsClient
} from '../services/ServerEvents';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever.js';
import type { PropertyValues } from 'lit';
import { PageElement, PageLocation } from '../helpers/page-element';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import {
  SilentGridRefresher,
  silentRefreshStyles
} from '../helpers/silent-grid-refresh';
import '@vaadin/tooltip';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import {
  stopMonitorHub,
  waitForMonitorHubStop
} from '../helpers/monitor-hub-connection';

const username = 'Username';
const status = 'Status';
const components = 'Components';
const project = 'Project';
const environment = 'EnvironmentName';
const buildNumber = 'BuildNumber';
const id = 'Id';

@customElement('page-monitor-requests')
export class PageMonitorRequests
  extends ResponsiveMixin(PageElement)
  implements IDeploymentsEventsClient
{
  @query('#grid') grid: Grid | undefined;

  private silentRefresh = new SilentGridRefresher(() => this.grid);

  private hubConnection: HubConnection | undefined;

  @property({ type: Boolean }) isLoading = true;

  @property({ type: Boolean }) isSearching = false;

  @property({ type: Boolean }) autoRefresh = true;

  @property({ type: String }) hubConnectionState: string | undefined =
    HubConnectionState.Disconnected;

  @state() noResults = false;

  // Keep reference to header root so we can manually re-render when reactive
  // properties (e.g. hubConnectionState, autoRefresh) change. Vaadin's
  // headerRenderer is only invoked when the cell is first created, so Lit's
  // normal re-render cycle does not update the header automatically.
  userFilter: string = '';
  statusFilter: string = '';
  componentsFilter: string = '';
  idFilter: string = '';
  projectFilter: string = '';
  envFilter: string = '';
  buildFilter: string = '';

  private monitorDataProvider = (
    params: GridDataProviderParams<DeploymentRequestApiModel>,
    callback: GridDataProviderCallback<DeploymentRequestApiModel>
  ) => {
    if (this.projectFilter !== '' && this.projectFilter !== undefined) {
      params.filters.push({ path: 'Project', value: this.projectFilter });
    }
    if (this.envFilter !== '' && this.envFilter !== undefined) {
      params.filters.push({
        path: 'EnvironmentName',
        value: this.envFilter
      });
    }
    if (this.buildFilter !== '' && this.buildFilter !== undefined) {
      params.filters.push({
        path: 'BuildNumber',
        value: this.buildFilter
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

    if (this.componentsFilter !== '' && this.componentsFilter !== undefined) {
      params.filters.push({
        path: 'Components',
        value: this.componentsFilter
      });
    }
    const api = new RequestStatusesApi(dorcApiConfiguration);
    this.silentRefresh.requestStarted();
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
          callback(
            data.Items ?? [],
            this.silentRefresh.reportedSize(data.TotalItems)
          );

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
          notification.setAttribute('errorMessage', errMessage);
          this.shadowRoot?.appendChild(notification);
          notification.open();
          console.error(errMessage, err);
          callback([], 0);
          this.silentRefresh.requestFinished();
          this.dispatchEvent(
            new CustomEvent('searching-requests-finished', {
              detail: { TotalItems: 0 },
              bubbles: true,
              composed: true
            })
          );
        },
        complete: () => {
          this.silentRefresh.requestFinished();
          this.monitorRequestsLoaded();
        }
      });
  };

  private stopHubConnection(): Promise<void> {
    if (!this.hubConnection) {
      return Promise.resolve();
    }
    return stopMonitorHub(
      this.hubConnection,
      state => (this.hubConnectionState = state)
    );
  }

  static get styles() {
    return [
      silentRefreshStyles,
      css`
        :host {
          display: flex;
          flex-direction: column;
          height: 100%;
          overflow: hidden;
          --divider-color: var(--dorc-border-color);
        }
        vaadin-grid {
          flex: 1;
          min-height: 0;
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

        .id-btn {
          font-size: 14px;
          font-family: monospace;
          background: color-mix(
            in srgb,
            var(--dorc-link-color, #58a6ff) 10%,
            transparent
          );
          color: var(--dorc-link-color, #58a6ff);
          display: inline-flex;
          align-items: center;
          gap: 5px;
          height: 24px;
          padding: 0 8px;
          margin: 2px 0;
          text-decoration: none;
          border-radius: 5px;
          border: 1px solid
            color-mix(in srgb, var(--dorc-link-color, #58a6ff) 28%, transparent);
          cursor: pointer;
          transition:
            background-color 0.15s ease,
            border-color 0.15s ease;
        }

        .id-btn:hover {
          background: color-mix(
            in srgb,
            var(--dorc-link-color, #58a6ff) 18%,
            transparent
          );
          border-color: color-mix(
            in srgb,
            var(--dorc-link-color, #58a6ff) 48%,
            transparent
          );
        }

        .details-primary {
          color: var(--lumo-body-text-color);
        }

        .details-secondary {
          font-size: var(--lumo-font-size-s);
          color: var(--lumo-secondary-text-color);
        }

        .timings-stack {
          display: flex;
          flex-direction: column;
          gap: 2px;
          line-height: var(--lumo-line-height-s);
          font-size: var(--lumo-font-size-s);
          color: var(--lumo-secondary-text-color);
        }

        .timings-row {
          display: flex;
          align-items: baseline;
          gap: 6px;
        }

        .timings-label {
          color: var(--dorc-text-secondary);
          min-width: 52px;
        }

        .cell-secondary {
          font-size: var(--lumo-font-size-s);
          color: var(--lumo-secondary-text-color);
        }

        .status-cell {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: var(--lumo-space-s);
          width: 100%;
          min-width: 245px;
        }

        .status-text {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          min-width: 85px;
          flex-shrink: 0;
        }

        .status-text::before {
          content: '';
          width: 6px;
          height: 6px;
          border-radius: 50%;
          background: currentColor;
          flex-shrink: 0;
        }

        .status-text.status-ok {
          color: var(--dorc-success-text, #3fb950);
        }

        .status-text.status-active {
          color: var(--dorc-link-color, #58a6ff);
        }

        .status-text.status-warn {
          color: var(--dorc-warning-text, #d29922);
        }

        .status-text.status-err {
          color: var(--dorc-error-color, #f85149);
        }

        .status-text.status-muted {
          color: var(--dorc-text-secondary, #8b949e);
        }

        .cover {
          object-fit: cover;
          position: fixed;
          top: 50%;
          left: 50%;
          transform: translate(-50%, -50%);
        }
      `
    ];
  }

  render() {
    return html`
      <dorc-spinner
        ?hidden="${!this.isLoading && !this.isSearching}"
      ></dorc-spinner>

      <connection-status-indicator
        .state="${this.hubConnectionState}"
        .autoRefresh="${this.autoRefresh}"
        @toggle-auto-refresh="${() => {
          void this.toggleAutoRefresh();
        }}"
      ></connection-status-indicator>

      <vaadin-grid
        id="grid"
        column-reordering-allowed
        multi-sort
        .size=${200}
        theme="compact row-stripes no-row-borders no-border"
        .dataProvider=${this.monitorDataProvider}
        style="z-index: 1"
      >
        <vaadin-grid-column
          path="Id"
          resizable
          auto-width
          ${columnHeaderRenderer(this.idHeaderRenderer, [])}
          ${columnBodyRenderer(this.idRenderer, [])}
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="Details"
          resizable
          auto-width
          ${columnHeaderRenderer(this.detailsHeaderRenderer, [])}
          ${columnBodyRenderer(this.detailsRenderer, [])}
        >
        </vaadin-grid-column>
        <vaadin-grid-column
          resizable
          ${columnBodyRenderer(this.timingsRenderer, [])}
          header="Timings"
          auto-width
          ?hidden="${this._narrowScreen}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="User"
          ${columnHeaderRenderer(this.usersHeaderRenderer, [])}
          ${columnBodyRenderer(this.usernameRenderer, [])}
          resizable
          auto-width
          ?hidden="${this._narrowScreen}"
        >
        </vaadin-grid-column>
        <vaadin-grid-column
          path="Status"
          header="Status"
          ${columnHeaderRenderer(this.statusHeaderRenderer, [])}
          ${columnBodyRenderer(this.statusRenderer, [])}
          resizable
          auto-width
        >
        </vaadin-grid-column>
        <vaadin-grid-column
          header="Components"
          ${columnHeaderRenderer(this.componentsHeaderRenderer, [])}
          ${columnBodyRenderer(this.componentsRenderer, [])}
          resizable
          auto-width
          ?hidden="${this._narrowScreen}"
        >
        </vaadin-grid-column>
      </vaadin-grid>
      <img
        class="cover"
        style="z-index: 2; height: 400px"
        ?hidden="${!this.noResults}"
        src="/modern-dorc-searching.webp"
        alt="No Results Found"
      />
    `;
  }

  protected async firstUpdated(
    _changedProperties: PropertyValues
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
    this.addEventListener(
      'request-paused',
      this.requestPaused as EventListener
    );
    this.addEventListener(
      'request-resumed',
      this.requestResumed as EventListener
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

  updated(changed: PropertyValueMap<any>) {
    super.updated(changed);
  }

  // Router lifecycle: feed location to PageElement -> html-meta-manager updates title/description
  public onAfterEnter(location: PageLocation) {
    this.location = location;
  }

  disconnectedCallback(): void {
    super.disconnectedCallback();
    if (this.hubConnection) {
      void this.stopHubConnection();
    }
  }

  private async initializeSignalR() {
    this.hubConnection = DeploymentHub.getConnection();
    await waitForMonitorHubStop(this.hubConnection);
    if (!this.isConnected) {
      return;
    }

    getReceiverRegister('IDeploymentsEventsClient').register(
      this.hubConnection,
      this
    );

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
      await this.hubConnection
        .start()
        .then(() => {
          this.hubConnectionState = this.hubConnection?.state;
        })
        .catch(err => {
          console.error('Error starting SignalR connection:', err);
          this.hubConnectionState = retrieveErrorMessage(err);
        });
    }
  }

  private debouncedRefreshGrid = this.debounce(() => this.refreshGrid(), 500);

  // Pausing stops the hub connection entirely so the client doesn't keep
  // (re)connecting in the background; resuming starts it again. SignalR's
  // automatic reconnect only kicks in on connection loss, not manual stop.
  private async toggleAutoRefresh() {
    this.autoRefresh = !this.autoRefresh;
    if (!this.hubConnection) return;
    if (this.autoRefresh) {
      await waitForMonitorHubStop(this.hubConnection);
      if (!this.autoRefresh) {
        return;
      }
      if (this.hubConnection.state === HubConnectionState.Disconnected) {
        try {
          await this.hubConnection.start();
        } catch (err) {
          if (!this.autoRefresh) {
            return;
          }
          console.error('Error starting SignalR connection:', err);
          this.hubConnectionState = retrieveErrorMessage(err);
          return;
        }
      }
      if (!this.autoRefresh) {
        await this.stopHubConnection();
        return;
      }
      this.hubConnectionState = this.hubConnection.state;
      this.refreshGrid();
    } else {
      void this.stopHubConnection();
      this.hubConnectionState = this.hubConnection.state;
    }
  }

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
    this.silentRefresh.refresh();
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
        case project:
          this.projectFilter = value;
          break;
        case environment:
          this.envFilter = value;
          break;
        case buildNumber:
          this.buildFilter = value;
          break;
        default:
          break;
      }
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
    requestAnimationFrame(() => {
      this.grid?.recalculateColumnWidths();
    });
  }

  updateGrid() {
    if (this.grid) {
      this.silentRefresh.refreshWithLoadingUi();
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

  requestPaused(e: CustomEvent) {
    Notification.show(`Paused request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }

  requestResumed(e: CustomEvent) {
    Notification.show(`Resumed request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }

  private componentsRenderer(item: DeploymentRequestApiModel) {
    const request = item as DeploymentRequestApiModel;
    const elements = request.Components?.split('|').sort((a, b) =>
      a.localeCompare(b)
    );

    return html`
      <vaadin-vertical-layout>
        ${elements?.map(
          element => html`<div class="cell-secondary">${element}</div>`
        )}
      </vaadin-vertical-layout>
    `;
  }

  private usernameRenderer(item: DeploymentRequestApiModel) {
    const request = item as DeploymentRequestApiModel;
    return html`<div class="cell-secondary">${request.UserName}</div>`;
  }

  private detailsRenderer = (item: DeploymentRequestApiModel) => {
    const request = item;
    return html`
      <vaadin-horizontal-layout style="align-items: center;" theme="spacing">
        <vaadin-vertical-layout>
          <div class="details-primary">
            ${request.Project} - ${request.EnvironmentName}
          </div>
          <div class="details-secondary">${request.BuildNumber}</div>
        </vaadin-vertical-layout>
      </vaadin-horizontal-layout>
    `;
  };

  private timingsRenderer = (item: DeploymentRequestApiModel) => {
    const request = item as DeploymentRequestApiModel;
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

    const started = `${sDate} ${sTime}`.trim();
    const finished = `${cDate} ${cTime}`.trim();

    return html`
      <div class="timings-stack">
        ${
          started
            ? html`<div class="timings-row">
                <span class="timings-label">Started</span>
                <span>${started}</span>
              </div>`
            : html`<div class="timings-row"></div>`
        }
        ${
          finished
            ? html`<div class="timings-row">
                <span class="timings-label">Finished</span>
                <span>${finished}</span>
              </div>`
            : html`<div class="timings-row"></div>`
        }
      </div>
    `;
  };

  private idRenderer = (item: DeploymentRequestApiModel) => {
    const request = item;
    return html`
      <button
        type="button"
        class="id-btn"
        @click="${(e: Event) => {
          e.stopPropagation();
          this.dispatchEvent(
            new CustomEvent('open-monitor-result', {
              detail: { request, message: 'Show results for Request' },
              bubbles: true,
              composed: true
            })
          );
        }}"
      >
        <span>${request.Id}</span>
        <svg
          width="11"
          height="11"
          viewBox="0 0 16 16"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          aria-hidden="true"
        >
          <path d="M6 3h7v7M13 3L6 10" />
        </svg>
      </button>
    `;
  };

  private statusToneClass(status: string | null | undefined): string {
    switch (status) {
      case 'Completed':
      case 'Complete':
        return 'status-ok';
      case 'Running':
      case 'Requesting':
      case 'Restarting':
        return 'status-active';
      case 'Pending':
      case 'Paused':
        return 'status-warn';
      case 'Failed':
      case 'Errored':
        return 'status-err';
      default:
        return 'status-muted';
    }
  }

  private statusRenderer = (item: DeploymentRequestApiModel) => {
    return html`
      <div class="status-cell">
        <span class="status-text ${this.statusToneClass(item.Status)}"
          >${item.Status}</span
        >
        ${this._requestControlsRenderer(item)}
      </div>
    `;
  };

  _requestControlsRenderer(item: DeploymentRequestApiModel) {
    return html` <request-controls
      .requestId=${item.Id ?? 0}
      .cancelable=${
        !!item.UserEditable &&
        (item.Status === 'Running' ||
          item.Status === 'Requesting' ||
          item.Status === 'Pending' ||
          item.Status === 'Restarting' ||
          item.Status === 'Paused')
      }
      .canRestart=${
        !!item.UserEditable &&
        item.Status !== 'Pending' &&
        item.Status !== 'Paused'
      }
      .canPause=${!!item.UserEditable && item.Status === 'Pending'}
      .canResume=${!!item.UserEditable && item.Status === 'Paused'}
    ></request-controls>`;
  }

  idHeaderRenderer = () => html`
    <vaadin-horizontal-layout
      style="align-items:center; gap:2px;"
      theme="spacing-xs"
    >
      <vaadin-grid-sorter
        path="Id"
        direction="desc"
        style="align-items: normal"
      ></vaadin-grid-sorter>

      <vaadin-text-field
        placeholder="Id"
        clear-button-visible
        focus-target
        style="width: 75px"
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
  `;

  detailsHeaderRenderer = () => {
    return html`
      <div style="display: flex; align-items: center; gap: 2px;">
        <vaadin-grid-sorter
          path="Project"
          style="align-items: normal; flex: 0 0 auto;"
        ></vaadin-grid-sorter>
        <vaadin-text-field
          placeholder="Project"
          title="starts with"
          clear-button-visible
          focus-target
          style="width: 90px;"
          theme="small"
          @input="${(e: InputEvent) => {
            const textField = e.target as any;
            this.dispatchEvent(
              new CustomEvent('searching-requests-started', {
                detail: {
                  field: project,
                  value: textField?.value
                },
                bubbles: true,
                composed: true
              })
            );
          }}"
        ></vaadin-text-field>
        <span style="flex: 0 0 auto; color: var(--lumo-secondary-text-color);"
          >-</span
        >
        <vaadin-grid-sorter
          path="EnvironmentName"
          style="align-items: normal; flex: 0 0 auto;"
        ></vaadin-grid-sorter>
        <vaadin-text-field
          placeholder="Environment"
          title="starts with"
          clear-button-visible
          focus-target
          style="width: 110px;"
          theme="small"
          @input="${(e: InputEvent) => {
            const textField = e.target as any;
            this.dispatchEvent(
              new CustomEvent('searching-requests-started', {
                detail: {
                  field: environment,
                  value: textField?.value
                },
                bubbles: true,
                composed: true
              })
            );
          }}"
        ></vaadin-text-field>
        <vaadin-grid-sorter
          path="BuildNumber"
          style="align-items: normal; flex: 0 0 auto;"
        ></vaadin-grid-sorter>
        <vaadin-text-field
          placeholder="Build#"
          title="contains"
          clear-button-visible
          focus-target
          style="width: 80px;"
          theme="small"
          @input="${(e: InputEvent) => {
            const textField = e.target as any;
            this.dispatchEvent(
              new CustomEvent('searching-requests-started', {
                detail: {
                  field: buildNumber,
                  value: textField?.value
                },
                bubbles: true,
                composed: true
              })
            );
          }}"
        ></vaadin-text-field>
      </div>
    `;
  };

  usersHeaderRenderer = () => {
    return html`
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
    `;
  };

  statusHeaderRenderer = () => {
    return html`
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
    `;
  };

  componentsHeaderRenderer = () => {
    return html`
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
    `;
  };
}
