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
import { GridFilter } from '@vaadin/grid/vaadin-grid-filter';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid-sorter';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/text-field';
import { css, PropertyValueMap, render } from 'lit';
import { customElement, property, query } from 'lit/decorators.js';
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
import { PageElement } from '../helpers/page-element';
import '@vaadin/vaadin-lumo-styles/typography.js';
import '../icons/iron-icons.js';
import { ErrorNotification } from '../components/notifications/error-notification';

@customElement('page-monitor-requests')
export class PageMonitorRequests extends PageElement {
  @property({ type: Array })
  requestStatuses: Array<DeploymentRequestApiModel> = [];

  @property({ type: Boolean }) details = false;

  @property() nameFilterValue = '';

  @query('#grid') grid: Grid | undefined;

  @property({ type: Boolean }) loading = true;

  @property({ type: Boolean }) searching = false;

  @property({ type: Boolean }) noResults = false;

  static get styles() {
    return css`
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 56px);
        --divider-color: rgb(223, 232, 239);
      }
      vaadin-grid#grid {
        overflow: hidden;
      }
      vaadin-text-field {
        padding: 0px;
        margin: 0px;
      }
      vaadin-grid-cell-content {
        padding-top: 0px;
        padding-bottom: 0px;
        margin: 0px;
      }
      paper-dialog.size-position {
        top: 16px;
        overflow: auto;
        padding: 10px;
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
      <div
        class="overlay"
        style="z-index: 2"
        ?hidden="${!(this.loading || this.searching)}"
      >
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
        theme="compact row-stripes no-row-borders no-border"
        .dataProvider="${this.getRequestStatuses}"
        ?hidden="${this.loading}"
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
          path="UserName"
          header="User"
          .headerRenderer="${this.usersHeaderRenderer}"
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
          path="Components"
          header="Components"
          .headerRenderer="${this.componentsHeaderRenderer}"
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

  protected firstUpdated(
    _changedProperties: PropertyValueMap<any> | Map<PropertyKey, unknown>
  ): void {
    super.firstUpdated(_changedProperties);

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
      'monitor-requests-loaded',
      this.monitorRequestsLoaded as EventListener
    );

    this.addEventListener(
      'searching-requests-started',
      this.searchingRequestsStarted as EventListener
    );

    this.addEventListener(
      'searching-requests-finished',
      this.searchingRequestsFinished as EventListener
    );
  }

  private searchingRequestsStarted() {
    this.searching = true;
  }

  private searchingRequestsFinished(e: CustomEvent) {
    const data: GetRequestStatusesListResponseDto = e.detail;
    if (data.TotalItems === 0) this.noResults = true;
    else this.noResults = false;

    this.searching = false;
  }

  private monitorRequestsLoaded() {
    this.loading = false;
  }

  updateGrid() {
    if (this.grid) {
      this.grid.clearCache();
      this.loading = true;
    }
  }

  requestCancelled(e: CustomEvent) {
    this.updateGrid();
    Notification.show(`Cancelled request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }

  requestRestarted(e: CustomEvent) {
    this.updateGrid();
    Notification.show(`Restarted request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
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
            <div>${`${sDate} ${sTime}`}</div>
            <div>${`${cDate} ${cTime}`}</div>
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
          <span> ${request.Id} </span>
          <vaadin-button
            title="View Detailed Results"
            theme="icon"
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
      html`<request-controls
        .requestId="${item.Id ?? 0}"
        .cancelable="${item.UserEditable &&
        (item.Status === 'Running' ||
          item.Status === 'Requesting' ||
          item.Status === 'Pending' ||
          item.Status === 'Restarting')}"
        .canRestart="${item.UserEditable && item.Status !== 'Pending'}"
      ></request-controls>`,
      root
    );
  }

  idHeaderRenderer(root: HTMLElement) {
    render(
      html`
        <vaadin-button
          theme="icon"
          style="padding: 0px; margin: 0px"
          @click="${() => {
            const event = new CustomEvent('refresh-requests', {
              detail: {},
              bubbles: true,
              composed: true
            });
            this.dispatchEvent(event);
          }}"
          ><vaadin-icon
            icon="icons:refresh"
            style="color: cornflowerblue"
          ></vaadin-icon
        ></vaadin-button>
        <vaadin-grid-sorter path="Id" direction="desc">Id</vaadin-grid-sorter>
        <vaadin-grid-filter path="Id">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100px"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>
      `,
      root
    );

    const filter: GridFilter = root.querySelector(
      'vaadin-grid-filter'
    ) as GridFilter;
    root
      .querySelector('vaadin-text-field')!
      .addEventListener('value-changed', (e: any) => {
        filter.value = e.detail.value;
        this.dispatchEvent(
          new CustomEvent('searching-requests-started', {
            detail: {},
            bubbles: true,
            composed: true
          })
        );
      });
  }

  detailsHeaderRenderer(root: HTMLElement) {
    render(
      html`
        Details
        <vaadin-grid-filter path="Details">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100px"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>
      `,
      root
    );

    const filter: GridFilter = root.querySelector(
      'vaadin-grid-filter'
    ) as GridFilter;
    root
      .querySelector('vaadin-text-field')!
      .addEventListener('value-changed', (e: any) => {
        filter.value = e.detail.value;
        this.dispatchEvent(
          new CustomEvent('searching-requests-started', {
            detail: {},
            bubbles: true,
            composed: true
          })
        );
      });
  }

  usersHeaderRenderer(root: HTMLElement) {
    render(
      html`
        User
        <vaadin-grid-filter path="Username">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100px"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>
      `,
      root
    );

    const filter: GridFilter = root.querySelector(
      'vaadin-grid-filter'
    ) as GridFilter;
    root
      .querySelector('vaadin-text-field')!
      .addEventListener('value-changed', (e: any) => {
        filter.value = e.detail.value;
        this.dispatchEvent(
          new CustomEvent('searching-requests-started', {
            detail: {},
            bubbles: true,
            composed: true
          })
        );
      });
  }

  statusHeaderRenderer(root: HTMLElement) {
    render(
      html`
        Status
        <vaadin-grid-filter path="Status">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100px"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>
      `,
      root
    );

    const filter: GridFilter = root.querySelector(
      'vaadin-grid-filter'
    ) as GridFilter;
    root
      .querySelector('vaadin-text-field')!
      .addEventListener('value-changed', (e: any) => {
        filter.value = e.detail.value;
        this.dispatchEvent(
          new CustomEvent('searching-requests-started', {
            detail: {},
            bubbles: true,
            composed: true
          })
        );
      });
  }

  componentsHeaderRenderer(root: HTMLElement) {
    render(
      html`
        Components
        <vaadin-grid-filter path="Components">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100px"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>
      `,
      root
    );

    const filter: GridFilter = root.querySelector(
      'vaadin-grid-filter'
    ) as GridFilter;
    root
      .querySelector('vaadin-text-field')!
      .addEventListener('value-changed', (e: any) => {
        filter.value = e.detail.value;
        this.dispatchEvent(
          new CustomEvent('searching-requests-started', {
            detail: {},
            bubbles: true,
            composed: true
          })
        );
      });
  }

  getRequestStatuses(
    params: GridDataProviderParams<DeploymentRequestApiModel>,
    callback: GridDataProviderCallback<DeploymentRequestApiModel>
  ) {
    const detailsIdx = params.filters.findIndex(
      filter => filter.path === 'Details'
    );

    if (detailsIdx !== -1) {
      const detailsValue = params.filters[detailsIdx].value;
      params.filters.splice(detailsIdx, 1);
      if (detailsValue !== '') {
        params.filters.push({ path: 'Project', value: detailsValue });
        params.filters.push({ path: 'EnvironmentName', value: detailsValue });
        params.filters.push({ path: 'BuildNumber', value: detailsValue });
      }
    }

    const idIdx = params.filters.findIndex(filter => filter.path === 'Id');
    if (idIdx !== -1) {
      const idValue = params.filters[idIdx].value;
      params.filters.splice(idIdx, 1);
      if (idValue !== '') {
        params.filters.push({ path: 'Id', value: idValue });
      }
    }

    const usernameIdx = params.filters.findIndex(
      filter => filter.path === 'Username'
    );
    if (usernameIdx !== -1) {
      const usernameValue = params.filters[usernameIdx].value;
      params.filters.splice(usernameIdx, 1);
      if (usernameValue !== '') {
        params.filters.push({ path: 'Username', value: usernameValue });
      }
    }

    const statusIdx = params.filters.findIndex(
      filter => filter.path === 'Status'
    );
    if (statusIdx !== -1) {
      const statusValue = params.filters[statusIdx].value;
      params.filters.splice(statusIdx, 1);
      if (statusValue !== '') {
        params.filters.push({ path: 'Status', value: statusValue });
      }
    }

    const componentsIdx = params.filters.findIndex(
      filter => filter.path === 'Components'
    );
    if (componentsIdx !== -1) {
      const componentsValue = params.filters[componentsIdx].value;
      params.filters.splice(componentsIdx, 1);
      if (componentsValue !== '') {
        params.filters.push({ path: 'Components', value: componentsValue });
      }
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
            item => (item.UserName = item.UserName?.split('\\')[1])
          );

          this.requestStatuses = data.Items as [];
          this.dispatchEvent(
            new CustomEvent('searching-requests-finished', {
              detail: data,
              bubbles: true,
              composed: true
            })
          );
          callback(this.requestStatuses ?? [], data.TotalItems);
        },
        error: (err: any) => {
          const notification = new ErrorNotification();
          notification.setAttribute(
            'errorMessage',
            err.response.ExceptionMessage
          );
          this.shadowRoot?.appendChild(notification);
          notification.open();
          console.error(err);
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
          this.dispatchEvent(
            new CustomEvent('monitor-requests-loaded', {
              detail: {},
              bubbles: true,
              composed: true
            })
          );
          console.log(
            `done loading request Statuses page:${params.page + Number(1)}`
          );
        }
      });
  }
}
