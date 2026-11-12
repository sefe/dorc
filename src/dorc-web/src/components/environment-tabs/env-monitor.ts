import { columnBodyRenderer, columnHeaderRenderer } from '@vaadin/grid/lit';
import type { Grid } from '@vaadin/grid';
import '../dorc-spinner';
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
import '../../components/grid-button-groups/request-controls';
import { Notification } from '@vaadin/notification';
import {
  DeploymentRequestApiModel,
  GetRequestStatusesListResponseDto,
  PagedDataFilter,
  PagedDataSorting,
  RequestStatusesApi
} from '../../apis/dorc-api';
import '../../icons/iron-icons.js';
import '../../icons/custom-icons.js';
import { ErrorNotification } from '../../components/notifications/error-notification';
import { getShortLogonName } from '../../helpers/user-extensions.js';
import '../../components/connection-status-indicator';
import {
  DeploymentHub,
  getReceiverRegister,
  IDeploymentsEventsClient
} from '../../services/ServerEvents';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { retrieveErrorMessage } from '../../helpers/errorMessage-retriever.js';
import type { PropertyValues } from 'lit';
import type { PageLocation } from '../../helpers/page-element';
import { PageEnvBase } from './page-env-base';
import { ResponsiveMixin } from '../../helpers/responsive-mixin';
import {
  SilentGridRefresher,
  silentRefreshStyles
} from '../../helpers/silent-grid-refresh';
import '@vaadin/tooltip';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';
import {
  stopMonitorHub,
  waitForMonitorHubStop
} from '../../helpers/monitor-hub-connection';

const username = 'Username';
const status = 'Status';
const components = 'Components';
const details = 'Details';
const id = 'Id';

@customElement('env-monitor')
export class EnvMonitor
  extends ResponsiveMixin(PageEnvBase)
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

  @state() protected location = {} as PageLocation;

  // Keep reference to header root so we can manually re-render when reactive
  // properties (e.g. hubConnectionState, autoRefresh) change. Vaadin's
  // headerRenderer is only invoked when the cell is first created, so Lit's
  // normal re-render cycle does not update the header automatically.
  userFilter: string = '';
  statusFilter: string = '';
  componentsFilter: string = '';
  idFilter: string = '';
  detailsFilter: string = '';

  private monitorDataProvider = (
    params: GridDataProviderParams<DeploymentRequestApiModel>,
    callback: GridDataProviderCallback<DeploymentRequestApiModel>
  ) => {
    if (params.sortOrders !== undefined && params.sortOrders.length > 1) {
      return;
    }

    if (this.environmentName) {
      params.filters.push({
        path: 'EnvironmentNameExact',
        value: this.environmentName
      });
    }

    if (this.detailsFilter !== '' && this.detailsFilter !== undefined) {
      params.filters.push({ path: 'Project', value: this.detailsFilter });
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

  constructor() {
    super();

    super.loadEnvironmentInfo();
  }

  static get styles() {
    return [
      silentRefreshStyles,
      css`
        :host {
          display: flex;
          flex-direction: column;
          width: 100%;
          height: 100%;
          overflow: hidden;
        }

        vaadin-grid {
          overflow: hidden;
          flex: 1;
          min-height: 0;
          height: 100%;
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

        vaadin-grid::part(row) {
          cursor: pointer;
        }

        .id-badge {
          font-size: var(--lumo-font-size-s);
          color: var(--dorc-link-color, #58a6ff);
          background: color-mix(
            in srgb,
            var(--dorc-link-color, #58a6ff) 10%,
            transparent
          );
          border: 1px solid
            color-mix(in srgb, var(--dorc-link-color, #58a6ff) 28%, transparent);
          border-radius: 5px;
          height: 24px;
          padding: 0 8px;
          display: inline-flex;
          align-items: center;
          gap: 5px;
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
        @media (max-width: 768px) {
          vaadin-grid-cell-content {
            white-space: normal;
            word-wrap: break-word;
            overflow-wrap: break-word;
          }
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
        theme="compact row-stripes no-row-borders no-border hover-highlight"
        @active-item-changed="${this.onRowClick}"
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

  connectedCallback(): void {
    super.connectedCallback();
    this.addEventListener('keydown', this._onHostKeyDown);
  }

  disconnectedCallback(): void {
    super.disconnectedCallback();
    this.removeEventListener('keydown', this._onHostKeyDown);
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
        case details:
          this.detailsFilter = value;
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

  private componentsRenderer(item: DeploymentRequestApiModel) {
    const request = item as DeploymentRequestApiModel;
    const elements = request.Components?.split('|');

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
      <span class="id-badge">
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
      </span>
    `;
  };

  private onRowClick = (e: CustomEvent) => {
    const request = e.detail.value as DeploymentRequestApiModel | null;
    if (!request) return;

    const grid = this.shadowRoot?.getElementById('grid') as Grid | null;
    if (grid) grid.activeItem = null;

    this.dispatchEvent(
      new CustomEvent('open-monitor-result', {
        detail: {
          request,
          message: 'Show results for Request'
        },
        bubbles: true,
        composed: true
      })
    );
  };

  // Enter on a focused body row opens the detail panel by activating the row,
  // which routes through the existing active-item-changed handler. Bails when
  // the grid is in interacting-mode (focus is inside a cell-internal control
  // such as a column-header filter input or sort button).
  private _onHostKeyDown = (e: KeyboardEvent) => {
    if (e.key !== 'Enter') return;
    const grid = this.shadowRoot?.getElementById('grid') as Grid | null;
    if (!grid || grid.hasAttribute('interacting')) return;

    const row = e
      .composedPath()
      .find(
        (el): el is HTMLElement =>
          el instanceof HTMLElement &&
          el.localName === 'tr' &&
          el.getAttribute('role') === 'row'
      );
    if (!row) return;

    const item = (row as unknown as { _item?: DeploymentRequestApiModel })
      ._item;
    if (!item) return;

    e.preventDefault();
    grid.activeItem = item;
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
      @click="${(e: Event) => e.stopPropagation()}"
      .requestId=${item.Id ?? 0}
      .cancelable=${
        !!item.UserEditable &&
        (item.Status === 'Running' ||
          item.Status === 'Requesting' ||
          item.Status === 'Pending' ||
          item.Status === 'Restarting')
      }
      .canRestart=${!!item.UserEditable && item.Status !== 'Pending'}
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
      <vaadin-text-field
        placeholder="Project / Build"
        title="Project starts with the entered text, or Build contains it"
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
