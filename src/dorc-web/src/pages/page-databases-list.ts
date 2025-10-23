import '@vaadin/button';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/grid/vaadin-grid-filter';
import '@vaadin/icon';
import '@vaadin/text-field';
import { css, PropertyValues, render } from 'lit';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/add-edit-database';
import '@vaadin/dialog';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { map } from 'lit/directives/map.js';
import { Notification } from '@vaadin/notification';
import { TextField } from '@vaadin/text-field';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import {
  Grid,
  GridDataProviderCallback,
  GridDataProviderParams,
  GridFilterDefinition,
  GridItemModel,
  GridSorterDefinition
} from '@vaadin/grid';
import {
  EnvironmentApiModel,
  PagedDataFilter,
  PagedDataSorting,
  RefDataEnvironmentsApi,
  DatabaseApiModel,
  type GetDatabaseApiModelListResponseDto
} from '../apis/dorc-api';
import { RefDataDatabasesApi } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { AttachedDatabases } from '../components/attached-databases';
import '../components/grid-button-groups/database-controls';
import '@vaadin/vaadin-lumo-styles/typography.js';
import '@vaadin/grid/vaadin-grid-sorter';
import { ErrorNotification } from '../components/notifications/error-notification';

const name = 'Name';
const type = 'Type';
const serverName = 'ServerName';
const environmentNames = 'EnvironmentNames';

@customElement('page-databases-list')
export class PageDatabasesList extends PageElement {
  @property({ type: Boolean }) loading = true;
  @property({ type: Boolean }) searching = false;
  @property({ type: Boolean }) noResults = false;

  @query('#grid') grid: Grid | undefined;

  @state()
  private addEditDatabaseDialogOpened = false;

  @property({ type: Object })
  selectedDatabase: DatabaseApiModel | undefined;

  environmentNamesFilter: string = '';
  nameFilter: string = '';
  typeFilter: string = '';
  serverNameFilter: string = '';

  static get styles() {
    return css`
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 56px);
        --divider-color: rgb(223, 232, 239);
      }
      vaadin-button {
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

      .tag {
        font-size: 14px;
        font-family: monospace;
        background-color: cornflowerblue;
        color: white;
        display: inline-block;
        padding: 3px;
        margin: 3px;
        text-decoration: none;
        border-radius: 3px;
      }

      .tag:hover {
        background-color: #444;
        color: #fea40f;
        cursor: pointer;
        text-decoration: none;
      }

      .env {
        font-size: 14px;
        border: 0px;
        font-family: monospace;
        background-color: var(
          --_lumo-button-background-color,
          var(--lumo-contrast-5pct)
        );
        color: var(--_lumo-button-color, var(--lumo-primary-text-color));
        display: inline-block;
        padding: 3px;
        margin: 3px;
        text-decoration: none;
        border-radius: 3px;
      }

      .env:hover {
        background-color: var(
          --_lumo-button-background-color,
          var(--lumo-contrast-10pct)
        );
        color: #fea40f;
        cursor: pointer;
        text-decoration: none;
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
      <vaadin-dialog
        id='add-edit-database-dialog'
        header-title='Add/Edit Database'
        .opened='${this.addEditDatabaseDialogOpened}'
        draggable
        @opened-changed='${(event: DialogOpenedChangedEvent) => {
          this.addEditDatabaseDialogOpened = event.detail.value;
          if (!this.addEditDatabaseDialogOpened) {
            this.selectedDatabase = {};
          }
        }}'
        ${dialogRenderer(this.renderAddEditDatabaseDialog, [this.selectedDatabase])}
        ${dialogFooterRenderer(this.renderAddEditDatabaseFooter, [])}
      ></vaadin-dialog>
      <div
        class='overlay'
        style='z-index: 2'
        ?hidden='${!(this.loading || this.searching)}'
      >
        <div class='overlay__inner'>
          <div class='overlay__content'>
            <span class='spinner'></span>
          </div>
        </div>
      </div>
      <vaadin-grid
        id='grid'
        .dataProvider='${this.debouncedDataProvider}'
        column-reordering-allowed
        multi-sort
        style='z-index: 1'
        ?hidden='${this.loading}'
        theme='compact row-stripes no-row-borders no-border'
      >
        <vaadin-grid-column
          path='ServerName'
          resizable
          .headerRenderer='${this.instanceHeaderRenderer}'
          style='color:lightgray'
          auto-width
          flex-grow='0'
        ></vaadin-grid-column>
        <vaadin-grid-column
          path='Name'
          resizable
          .headerRenderer='${this.dbNameHeaderRenderer}'
          auto-width
          flex-grow='0'
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer='${this.applicationTagsRenderer}'
          resizable
          .headerRenderer='${this.appTagsHeaderRenderer}'
        ></vaadin-grid-column>
        <vaadin-grid-column
          width='300px'
          flex-grow='0'
          .renderer='${this.environmentNamesRenderer}'
          .headerRenderer='${this.environmentNamesHeaderRenderer}'
          resizable
          header='Mapped Environments'
        ></vaadin-grid-column>
        <vaadin-grid-column
          width='200px'
          flex-grow='0'
          resizable
          .renderer='${this._boundDatabasesButtonsRenderer}'
          .headerRenderer='${this.buttonsHeaderRenderer}'
        >
      </vaadin-grid>
      <img
        class='cover'
        style='z-index: 2; height: 400px'
        ?hidden='${!this.noResults}'
        src='/hegsie_white_background_cartoon_geek_code_simple_icon_searching_12343b57-9c4e-45c6-b2f3-7765e8596718.png'
        alt='No Results Found'
      />
    `;
  }

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

  debouncedDataProvider = this.debounce(
    (
      params: GridDataProviderParams<DatabaseApiModel>,
      callback: GridDataProviderCallback<DatabaseApiModel>
    ) => {
      if (this.nameFilter !== '' && this.nameFilter !== undefined) {
        params.filters.push({ path: 'Name', value: this.nameFilter });
      }

      if (this.typeFilter !== '' && this.typeFilter !== undefined) {
        params.filters.push({ path: 'Type', value: this.typeFilter });
      }

      if (this.serverNameFilter !== '' && this.serverNameFilter !== undefined) {
        params.filters.push({
          path: 'ServerName',
          value: this.serverNameFilter
        });
      }

      if (
        this.environmentNamesFilter !== '' &&
        this.environmentNamesFilter !== undefined
      ) {
        params.filters.push({
          path: 'EnvironmentNames',
          value: this.environmentNamesFilter
        });
      }

      const api = new RefDataDatabasesApi();
      api
        .refDataDatabasesByPagePut({
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
          next: (data: GetDatabaseApiModelListResponseDto) => {
            this.dispatchEvent(
              new CustomEvent('searching-databases-finished', {
                detail: data,
                bubbles: true,
                composed: true
              })
            );
            callback(data.Items ?? [], data.TotalItems);
          },
          error: (err: any) => console.error(err),
          complete: () => {
            this.dispatchEvent(
              new CustomEvent('databases-loaded', {
                detail: {},
                bubbles: true,
                composed: true
              })
            );
          }
        });
    },
    300
  );

  private debouncedInputHandler = this.debounce(
    (field: string, value: string) => {
      switch (field) {
        case name:
          this.nameFilter = value;
          break;
        case type:
          this.typeFilter = value;
          break;
        case serverName:
          this.serverNameFilter = value;
          break;
        case environmentNames:
          this.environmentNamesFilter = value;
          break;
        default:
          break;
      }
      this.grid?.clearCache();
      this.searching = true;
    },
    400 // debounce wait time
  );

  private renderAddEditDatabaseDialog = () => html`
    <add-edit-database
      id="add-edit-database"
      .database="${this.selectedDatabase}"
      @database-updated="${this.databaseUpdated}"
      @database-created="${this.databaseCreated}"
    ></add-edit-database>
  `;

  private renderAddEditDatabaseFooter = () => html`
    <vaadin-button @click="${this.closeAddEditDatabaseDialog}"
      >Close</vaadin-button
    >
  `;

  private closeAddEditDatabaseDialog() {
    this.addEditDatabaseDialogOpened = false;
  }

  private databasesLoaded() {
    this.loading = false;
  }

  private searchingDatabasesStarted(event: CustomEvent) {
    if (event.detail.value !== undefined) {
      this.debouncedInputHandler(event.detail.field, event.detail.value);
    }
  }

  private searchingDatabasesFinished(e: CustomEvent) {
    const data: GetDatabaseApiModelListResponseDto = e.detail;
    this.noResults = data.TotalItems === 0;

    this.searching = false;
  }

  updateGrid() {
    if (this.grid) {
      this.grid.clearCache();
      this.loading = true;
    }
  }

  buttonsHeaderRenderer(root: HTMLElement) {
    render(
      html`
        <vaadin-button
          title="Add Database"
          theme="small"
          @click="${() => {
            const event = new CustomEvent('open-add-edit-database-dialog', {
              detail: {},
              bubbles: true,
              composed: true
            });
            this.dispatchEvent(event);
          }}"
        >
          <vaadin-icon
            icon="vaadin:database"
            style="color: cornflowerblue"
          ></vaadin-icon>
          Add Database...
        </vaadin-button>
      `,
      root
    );
  }

  environmentNamesHeaderRenderer(root: HTMLElement) {
    render(
      html`
        <vaadin-text-field
          placeholder="Environments"
          clear-button-visible
          focus-target
          style="width: 120px"
          theme="small"
          @input="${(e: InputEvent) => {
            const textField = e.target as TextField;
            this.dispatchEvent(
              new CustomEvent('searching-databases-started', {
                detail: {
                  field: environmentNames,
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

  instanceHeaderRenderer(root: HTMLElement) {
    render(
      html`<vaadin-grid-sorter
          direction="asc"
          path="ServerName"
          style="align-items: normal"
        ></vaadin-grid-sorter>
        <vaadin-text-field
          placeholder="Instance"
          clear-button-visible
          focus-target
          style="width: 120px"
          theme="small"
          @input="${(e: InputEvent) => {
            const textField = e.target as TextField;
            this.dispatchEvent(
              new CustomEvent('searching-databases-started', {
                detail: {
                  field: serverName,
                  value: textField?.value
                },
                bubbles: true,
                composed: true
              })
            );
          }}"
        ></vaadin-text-field> `,
      root
    );
  }

  dbNameHeaderRenderer(root: HTMLElement) {
    render(
      html`<vaadin-grid-sorter
          path="Name"
          style="align-items: normal"
        ></vaadin-grid-sorter>
        <vaadin-text-field
          placeholder="Database"
          clear-button-visible
          focus-target
          style="width: 120px"
          theme="small"
          @input="${(e: InputEvent) => {
            const textField = e.target as TextField;
            this.dispatchEvent(
              new CustomEvent('searching-databases-started', {
                detail: {
                  field: name,
                  value: textField?.value
                },
                bubbles: true,
                composed: true
              })
            );
          }}"
        ></vaadin-text-field> `,
      root
    );
  }

  appTagsHeaderRenderer(root: HTMLElement) {
    render(
      html`<vaadin-grid-sorter
          path="Type"
          style="align-items: normal"
        ></vaadin-grid-sorter>
        <vaadin-text-field
          placeholder="Application Tag"
          clear-button-visible
          focus-target
          style="width: 120px"
          theme="small"
          @input="${(e: InputEvent) => {
            const textField = e.target as TextField;
            this.dispatchEvent(
              new CustomEvent('searching-databases-started', {
                detail: {
                  field: type,
                  value: textField?.value
                },
                bubbles: true,
                composed: true
              })
            );
          }}"
        ></vaadin-text-field> `,
      root
    );
  }

  private environmentNamesRenderer = (
    root: HTMLElement,
    _column: HTMLElement,
    model: GridItemModel<DatabaseApiModel>
  ) => {
    const database = model.item;
    const envNames = database.EnvironmentNames?.sort();

    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing">
          <vaadin-vertical-layout
            style="line-height: var(--lumo-line-height-s);"
          >
            ${map(
              envNames,
              (i: string) =>
                html` <button
                  class="env"
                  @click="${() =>
                    this.dispatchEvent(
                      new CustomEvent('open-environment-details', {
                        detail: {
                          envName: i
                        },
                        bubbles: true,
                        composed: true
                      })
                    )}"
                  style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
                >
                  ${i}
                </button>`
            )}
          </vaadin-vertical-layout>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  openEnvironmentDetails(event: CustomEvent) {
    const api2 = new RefDataEnvironmentsApi();
    api2.refDataEnvironmentsGet({ env: event.detail.envName }).subscribe({
      next: (data: EnvironmentApiModel[]) => {
        if (data[0] !== null) {
          const event = new CustomEvent('open-env-detail', {
            detail: {
              Environment: data[0]
            },
            bubbles: true,
            composed: true
          });
          this.dispatchEvent(event);
        } else {
          const notification = new ErrorNotification();
          notification.setAttribute(
            'errorMessage',
            'No Environment Information located'
          );
          this.shadowRoot?.appendChild(notification);
          notification.open();
        }
      },
      error: (err: any) => {
        if (err.status === 403) {
          const notification = new ErrorNotification();
          notification.setAttribute('errorMessage', err.response);
          this.shadowRoot?.appendChild(notification);
          notification.open();
        }
        console.error(err);
      },
      complete: () => console.log('done loading environment')
    });
  }

  _boundDatabasesButtonsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<AttachedDatabases>
  ) {
    const database = model.item as DatabaseApiModel;
    render(
      html` <database-controls
        .envId="${0}"
        .readonly="${!database.UserEditable}"
        .databaseDetails="${database}"
      >
      </database-controls>`,
      root
    );
  }

  private applicationTagsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DatabaseApiModel>
  ) => {
    const database = model.item;
    const appTags =
      database.Type !== undefined &&
      database.Type !== null &&
      database.Type.length > 0
        ? database.Type?.split(';')
        : [];

    render(
      html`
        ${map(
          appTags,
          value =>
            html` <button
              style="border: 0px"
              class="tag"
              @click="${() =>
                this.dispatchEvent(
                  new CustomEvent('filter-tags-database-list', {
                    detail: {
                      value
                    },
                    bubbles: true,
                    composed: true
                  })
                )}"
            >
              ${value}
            </button>`
        )}
      `,
      root
    );
  };

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'edit-database',
      this.openEditDatabaseDialog as EventListener
    );
    this.addEventListener(
      'manage-database-tags',
      this.openManageDatabaseTagsDialog as EventListener
    );
    this.addEventListener(
      'database-deleted',
      this.databaseDeleted as EventListener
    );
    this.addEventListener(
      'filter-tags-database-list',
      this.filterTagsDatabaseList as EventListener
    );

    this.addEventListener(
      'refresh-databases',
      this.updateGrid as EventListener
    );

    this.addEventListener(
      'databases-loaded',
      this.databasesLoaded as EventListener
    );

    this.addEventListener(
      'searching-databases-started',
      this.searchingDatabasesStarted as EventListener
    );

    this.addEventListener(
      'searching-databases-finished',
      this.searchingDatabasesFinished as EventListener
    );

    this.addEventListener(
      'open-add-edit-database-dialog',
      this.openAddEditDatabaseDialog as EventListener
    );
    this.addEventListener(
      'open-environment-details',
      this.openEnvironmentDetails as EventListener
    );
  }

  filterTagsDatabaseList(e: CustomEvent) {
    const search = this.shadowRoot?.getElementById('tags-search') as TextField;
    search.value = e.detail.value;
  }

  databaseDeleted(e: CustomEvent) {
    Notification.show(`Database ${e.detail.database.Name} deleted`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
    this.updateGrid();
  }

  databaseCreated(e: CustomEvent) {
    this.updateGrid();
    Notification.show(`Database ${e.detail.data.Name} created`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
    this.addEditDatabaseDialogOpened = false;
  }

  openEditDatabaseDialog(e: CustomEvent) {
    this.selectedDatabase = e.detail.database;
    this.addEditDatabaseDialogOpened = true;
  }

  openManageDatabaseTagsDialog(e: CustomEvent) {
    this.selectedDatabase = e.detail.database;
  }

  databaseUpdated(e: CustomEvent) {
    this.updateGrid();
    Notification.show(`Database details updated for ${e.detail.data.Name}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
    this.addEditDatabaseDialogOpened = false;
  }

  private openAddEditDatabaseDialog() {
    this.selectedDatabase = this.getEmptyDatabase();
    this.addEditDatabaseDialogOpened = true;
  }

  private getEmptyDatabase(): DatabaseApiModel {
    return {
      AdGroup: '',
      ArrayName: '',
      ServerName: '',
      Type: '',
      Name: '',
      EnvironmentNames: [],
      Id: 0,
      UserEditable: true
    };
  }
}
