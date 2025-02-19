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
import { GridFilter } from '@vaadin/grid/vaadin-grid-filter';
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
  DatabaseApiModel, type GetDatabaseApiModelListResponseDto
} from '../apis/dorc-api';
import { RefDataDatabasesApi } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { AttachedDatabases } from '../components/attached-databases';
import '../components/grid-button-groups/database-controls';
import '@vaadin/vaadin-lumo-styles/typography.js';
import '@vaadin/grid/vaadin-grid-sorter';
import { ErrorNotification } from '../components/notifications/error-notification';

@customElement('page-databases-list')
export class PageDatabasesList extends PageElement {
  @property({ type: Boolean }) loading = true;

  @property({ type: Boolean }) searching = false;

  @query('#grid') grid: Grid | undefined;

  @property({ type: Boolean }) noResults = false;

  @state()
  private addEditDatabaseDialogOpened = false;

  @property({ type: Object })
  selectedDatabase: DatabaseApiModel | undefined;

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

      paper-dialog.size-position {
        top: 16px;
        overflow: auto;
        padding: 10px;
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
        .dataProvider='${this.getDatabasesByPage}'
        column-reordering-allowed
        multi-sort
        style='z-index: 1'
        ?hidden='${this.loading}'
        theme='compact row-stripes no-row-borders no-border'
      >
        <vaadin-grid-column
          
          path='ServerName'
          resizable
          .headerRenderer='${this.nameHeaderRenderer}'
          style='color:lightgray'
          auto-width
          flex-grow='0'
        ></vaadin-grid-column>
        <vaadin-grid-column
          path='Name'
          resizable
          .headerRenderer='${this.osHeaderRenderer}'
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
          .databasesPage='${this}'
          resizable
          header='Mapped Environments'
        ></vaadin-grid-column>
        <vaadin-grid-column
          width='200px'
          flex-grow='0'
          resizable
          .renderer='${this._boundDatabasesButtonsRenderer}'
          .headerRenderer='${this.buttonsHeaderRenderer}'
          .databasesPage='${this}'
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

  updated(_changedProperties: PropertyValues) {
    super.updated(_changedProperties);

    console.log('updated page-databases-list ' + [..._changedProperties.entries()]);
  }

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

  private searchingDatabasesStarted() {
    this.searching = true;
  }

  private searchingDatabasesFinished(e: CustomEvent) {
    const data: GetDatabaseApiModelListResponseDto = e.detail;
    if (data.TotalItems === 0) this.noResults = true;
    else this.noResults = false;

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
      html` Environments
        <vaadin-grid-filter path="EnvironmentNames">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100%"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>`,
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
          new CustomEvent('searching-databases-started', {
            detail: {},
            bubbles: true,
            composed: true
          })
        );
      });
  }

  nameHeaderRenderer(root: HTMLElement) {
    render(
      html` <vaadin-grid-sorter direction="asc" path="ServerName"
          >Instance</vaadin-grid-sorter
        >
        <vaadin-grid-filter path="ServerName">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100%"
            theme="extra-small"
          ></vaadin-text-field>
        </vaadin-grid-filter>`,
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
          new CustomEvent('searching-databases-started', {
            detail: {},
            bubbles: true,
            composed: true
          })
        );
      });
  }

  osHeaderRenderer(root: HTMLElement) {
    render(
      html` <vaadin-grid-sorter path="Name"
          >Database</vaadin-grid-sorter
        >
        <vaadin-grid-filter path="Name">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100%"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>`,
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
          new CustomEvent('searching-databases-started', {
            detail: {},
            bubbles: true,
            composed: true
          })
        );
      });
  }

  appTagsHeaderRenderer(root: HTMLElement) {
    render(
      html` <vaadin-grid-sorter path="Type"
          >Application Tag</vaadin-grid-sorter
        >
        <vaadin-grid-filter path="Type">
          <vaadin-text-field
            id="tags-search"
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100%"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>`,
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
          new CustomEvent('searching-databases-started', {
            detail: {},
            bubbles: true,
            composed: true
          })
        );
      });
  }

  private environmentNamesRenderer = (
    root: HTMLElement,
    _column: HTMLElement,
    model: GridItemModel<DatabaseApiModel>
  ) => {
    const database = model.item;
    const envNames = database.EnvironmentNames?.sort();
    // The below line has a horrible hack
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const altThis = _column.databasesPage as PageDatabasesList;
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
                  @click="${() => altThis.openEnvironmentDetails(i)}"
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

  openEnvironmentDetails(envName: string) {
    const api2 = new RefDataEnvironmentsApi();
    api2.refDataEnvironmentsGet({ env: envName }).subscribe({
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

    this.addEventListener('refresh-databases', this.updateGrid as EventListener);

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

  getDatabasesByPage(
    params: GridDataProviderParams<DatabaseApiModel>,
    callback: GridDataProviderCallback<DatabaseApiModel>
  ) {
    const pathNames = ['Name', 'Type', 'ServerName', 'EnvironmentNames'];
    pathNames.forEach(x => {
      const idIdx = params.filters.findIndex(filter => filter.path === x);
      if (idIdx !== -1) {
        const idValue = params.filters[idIdx].value;
        params.filters.splice(idIdx, 1);
        if (idValue !== '') {
          params.filters.push({ path: x, value: idValue });
        }
      }
    });

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
          console.log(`done loading databases page:${params.page + Number(1)}`);
        }
      });
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
