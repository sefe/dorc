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
          background-color: var(--dorc-chip-bg);
          color: var(--dorc-chip-text);
          display: inline-block;
          padding: 3px;
          margin: 3px;
          text-decoration: none;
          border-radius: 3px;
          border: 0;
          cursor: pointer;
        }

        .id-btn:hover {
          background-color: var(--dorc-badge-bg);
          color: var(--dorc-badge-text);
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
          ${columnHeaderRenderer(this.idHeaderRenderer, [
            this.hubConnectionState,
            this.autoRefresh
          ])}
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
          resizable
          auto-width
        >
        </vaadin-grid-column>
        <vaadin-grid-column
          ${columnBodyRenderer(this._requestControlsRenderer, [])}
          resizable
          width="160px"
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
        src="/hegsie_white_background_cartoon_geek_code_simple_icon_searching_12343b57-9c4e-45c6-b2f3-7765e8596718.png"
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
      this.hubConnection.stop().catch(err => {
        console.error('Error stopping SignalR connection:', err);
      });
    }
  }

  private async initializeSignalR() {
    this.hubConnection = DeploymentHub.getConnection();

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
          this.hubConnectionState = err.toString();
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
      if (this.hubConnection.state === HubConnectionState.Disconnected) {
        try {
          await this.hubConnection.start();
        } catch (err) {
          console.error('Error starting SignalR connection:', err);
          this.hubConnectionState = String(err);
          return;
        }
      }
      this.hubConnectionState = this.hubConnection.state;
      this.refreshGrid();
    } else {
      this.hubConnection.stop().catch(err => {
        console.error('Error stopping SignalR connection:', err);
      });
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
          element =>
            html`<div
              style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
            >
              ${element}
            </div>`
        )}
      </vaadin-vertical-layout>
    `;
  }

  private usernameRenderer(item: DeploymentRequestApiModel) {
    const request = item as DeploymentRequestApiModel;
    return html` <div
      style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
    >
      ${request.UserName}
    </div>`;
  }

  private detailsRenderer = (item: DeploymentRequestApiModel) => {
    const request = item;
    return html`
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

    return html`
      <vaadin-horizontal-layout style="align-items: center;" theme="spacing">
        <vaadin-vertical-layout style="line-height: var(--lumo-line-height-s);">
          <div
            style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
          >
            ${`${sDate} ${sTime}`}
          </div>
          <div
            style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
          >
            ${`${cDate} ${cTime}`}
          </div>
        </vaadin-vertical-layout>
      </vaadin-horizontal-layout>
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
        ${request.Id}
      </button>
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
      <connection-status-indicator
        mode="toggle"
        .state="${this.hubConnectionState}"
        .autoRefresh="${this.autoRefresh}"
        @toggle-auto-refresh="${() => {
          void this.toggleAutoRefresh();
        }}"
      ></connection-status-indicator>

      ${
        !this.autoRefresh
          ? html`
              <vaadin-button
                theme="icon small"
                style="padding:0;margin:0"
                aria-label="Manual refresh"
                @click="${() => {
                  const event = new CustomEvent('refresh-requests', {
                    detail: {},
                    bubbles: true,
                    composed: true
                  });
                  this.dispatchEvent(event);
                }}"
              >
                <vaadin-tooltip
                  slot="tooltip"
                  text="Manual refresh"
                ></vaadin-tooltip>
                <vaadin-icon
                  icon="icons:refresh"
                  style="color: var(--dorc-link-color)"
                ></vaadin-icon>
              </vaadin-button>
            `
          : null
      }

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
          title="contains"
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
          title="contains"
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
