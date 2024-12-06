import '@polymer/paper-dialog';
import { PaperDialogElement } from '@polymer/paper-dialog';
import '@vaadin/button';
import '@vaadin/grid';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, LitElement, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/edit-database-permissions';
import './grid-button-groups/database-env-controls.ts';
import '../components/view-database-permissions';
import {
  DatabaseApiModel,
  EnvironmentContentApiModel,
  RefDataEnvironmentsDetailsApi
} from '../apis/dorc-api';
import { EditDatabasePermissions } from './edit-database-permissions';
import { ViewDatabasePermissions } from './view-database-permissions';
import { map } from 'lit/directives/map.js';

@customElement('attached-databases')
export class AttachedDatabases extends LitElement {
  @property({ type: Object })
  envContent: EnvironmentContentApiModel | undefined;

  @property({ type: Array })
  databases: Array<DatabaseApiModel> | undefined = [];

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) private readonly = true;

  static get styles() {
    return css`
      .center {
        margin: 10px 20px 10px;
        width: 50%;
        padding: 10px;
      }

      .inline {
        display: inline-block;
        vertical-align: middle;
      }

      paper-dialog.size-position {
        top: 16px;
        overflow: auto;
        padding: 10px;
      }

      vaadin-grid#grid {
        overflow: hidden;
        width: calc(100% - 4px);
        --divider-color: rgb(223, 232, 239);
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
    `;
  }

  render() {
    return html`
      <vaadin-grid
        id="grid"
        .items=${this.databases}
        theme="compact row-stripes no-row-borders no-border"
        all-rows-visible
      >
        <vaadin-grid-column
          path="ServerName"
          header="Instance"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Name"
          header="Database"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this.applicationTagsRenderer}"
          resizable
          header="Application Tag"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="ArrayName"
          header="Array Name"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this._boundDatabasesButtonsRenderer}"
          .attachedDbsControl="${this}"
          resizable
        >
        </vaadin-grid-column>
      </vaadin-grid>

      <paper-dialog
        class="size-position"
        id="permissions"
        allow-click-through
        modal
      >
        <edit-database-permissions
          id="edit"
          .envId="${this.envId}"
        ></edit-database-permissions>
        <div style="display: flex; justify-content: flex-end">
          <vaadin-button dialog-confirm>Close</vaadin-button>
        </div>
      </paper-dialog>

      <paper-dialog
        class="size-position"
        id="viewPermissions"
        allow-click-through
        modal
      >
        <view-database-permissions
          id="view"
          .envId="${this.envId}"
          .readonly="${this.readonly}"
        ></view-database-permissions>
        <div style="display: flex; justify-content: flex-end">
          <vaadin-button dialog-confirm>Close</vaadin-button>
        </div>
      </paper-dialog>
    `;
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

  _boundDatabasesButtonsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DatabaseApiModel>
  ) {
    const db = model.item as DatabaseApiModel;

    // The below line has a horrible hack
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const altThis = _column.attachedDbsControl as AttachedDatabases;
    render(
      html` <database-env-controls
        .dbDetails="${db}"
        .envId="${altThis.envId}"
        .readonly="${altThis.readonly}"
        @database-detached="${() => {
          altThis.refreshDatabases();
        }}"
        @manage-database-perms="${() => {
          const edit = altThis.shadowRoot?.getElementById(
            'edit'
          ) as EditDatabasePermissions;
          edit.reset();
          edit.setDbId(db.Id || 0);
          altThis.openDialog('permissions');
        }}"
        @view-database-perms="${() => {
          const view = altThis.shadowRoot?.getElementById(
            'view'
          ) as ViewDatabasePermissions;
          view.setDbId(db.Id || 0);
          view.loadDatabaseUsers();
          altThis.openDialog('viewPermissions');
        }}"
      ></database-env-controls>`,
      root
    );
  }

  setEnvironmentDetails(envDetails: EnvironmentContentApiModel) {
    this.envContent = envDetails;
    this.databases = envDetails.DbServers?.sort(this.sortDbs);
  }

  sortDbs(a: DatabaseApiModel, b: DatabaseApiModel): number {
    if (String(a.ServerName) > String(b.ServerName)) return 1;
    if (a.ServerName === b.ServerName) {
      if (String(a.Name) > String(b.Name)) return 1;
      return -1;
    }
    return -1;
  }

  refreshDatabases() {
    const api = new RefDataEnvironmentsDetailsApi();
    api.refDataEnvironmentsDetailsIdGet({ id: this.envId }).subscribe(
      (data: EnvironmentContentApiModel) => {
        this.setEnvironmentDetails(data);
      },
      (err: any) => console.error(err),
      () => console.log('done loading env details')
    );
  }

  openDialog(name: string) {
    const dialog = this.shadowRoot?.getElementById(name) as PaperDialogElement;
    dialog.open();
  }
}
