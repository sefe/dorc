import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/button';
import '@vaadin/grid';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, LitElement, render } from 'lit';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import './grid-button-groups/database-env-controls.ts';
import './grid-button-groups/server-controls';
import '../components/server-tags';
import './server-tags';
import '../components/add-edit-server';
import './add-edit-server';
import { Notification } from '@vaadin/notification';
import { map } from 'lit/directives/map.js';
import '../components/hegs-dialog';
import { HegsDialog } from './hegs-dialog';
import { ServerApiModel } from '../apis/dorc-api';
import type { EnvironmentContentApiModel } from '../apis/dorc-api';
import { splitTags } from '../helpers/tag-parser';

@customElement('attached-servers')
export class AttachedServers extends LitElement {
  @property({ type: Object })
  envContent!: EnvironmentContentApiModel;

  @property({ type: Array })
  servers: Array<ServerApiModel> | undefined = [];

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) private readonly = true;

  @property({ type: Object })
  selectedServer: ServerApiModel | undefined;

  @query('#add-edit-server-dialog') serverDialog!: HegsDialog;

  @query('#tags-dialog') tagsDialog!: HegsDialog;

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
        overflow: auto;
        width: calc(100% - 4px);
        --divider-color: rgb(223, 232, 239);
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
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
      vaadin-grid-cell-content {
      white-space: normal;
      word-wrap: break-word;
      overflow-wrap: break-word;
    }
    .column-content {
      display: block;
      width: 100%;
    }
    `;
  }

  render() {
    return html`
      <hegs-dialog id="add-edit-server-dialog" title="Add/Edit Server">
        <add-edit-server
          id="add-edit-server"
          .srv="${this.selectedServer}"
          @server-updated="${this.serverUpdated}"
        ></add-edit-server>
      </hegs-dialog>

      <hegs-dialog
        id="tags-dialog"
        title="Edit Server Tags for ${this.selectedServer?.Name}"
      >
        <server-tags
          id="tags"
          .server="${this.selectedServer}"
          @server-tags-updated="${this.serverTagsUpdated}"
        ></server-tags>
      </hegs-dialog>
      
      <vaadin-grid
        id="grid"
        .items=${this.servers}
        theme="compact row-stripes no-row-borders no-border"
        multi-sort
        all-rows-visible
      >
        <vaadin-grid-column
          path="Name"
          header="Server Name"
          resizable
          auto-width
          flex-grow="0"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="OsName"
          header="Operating System"
          resizable
          auto-width
          flex-grow="0"
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this.applicationTagsRenderer}"
          header="Application Tags"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          width="200px"
          flex-grow="0"
          .renderer="${this._boundServersButtonsRenderer}"
          .attachedServersControl="${this}"
        >
        </vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  private applicationTagsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<ServerApiModel>
  ) => {
    const server = model.item;
    const appTags = splitTags(server.ApplicationTags);

    render(
      html`
        ${map(
          appTags,
          value =>
            html`<button style="border: 0px" class="tag">${value}</button>`
        )}
      `,
      root
    );
  };

  serverUpdated(e: CustomEvent) {
    this.dispatchEvent(
      new CustomEvent('environment-stale', {
        bubbles: true,
        composed: true,
        detail: {}
      })
    );
    Notification.show(`Server details updated for ${e.detail.data.Name}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
    this.serverDialog.close();
  }

  serverTagsUpdated() {
    this.dispatchEvent(
      new CustomEvent('environment-stale', {
        bubbles: true,
        composed: true,
        detail: {}
      })
    );
    this.tagsDialog.close();
  }

  _boundServersButtonsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<AttachedServers>
  ) {
    // The below line has a horrible hack
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const altThis = _column.attachedServersControl as AttachedServers;
    const server = model.item as ServerApiModel;
    render(
      html` <server-controls
        .envId="${altThis.envId}"
        .envSet="${true}"
        .serverDetails="${server}"
        .readonly="${altThis.readonly}"
        @server-detached="${() => {
          Notification.show('Server detached', {
            theme: 'success',
            position: 'bottom-start',
            duration: 5000
          });
          this.dispatchEvent(
            new CustomEvent('environment-stale', {
              bubbles: true,
              composed: true,
              detail: {}
            })
          );
        }}"
        @server-deleted="${(e: CustomEvent) => {
          Notification.show(`Server ${e.detail.server.Name} deleted`, {
            theme: 'success',
            position: 'bottom-start',
            duration: 5000
          });
          this.dispatchEvent(
            new CustomEvent('environment-stale', {
              bubbles: true,
              composed: true,
              detail: {}
            })
          );
        }}"
        @manage-server-tags="${() => {
          altThis.openEditServerTags(server);
        }}"
        @edit-server="${(e: CustomEvent) => {
          altThis.editServer(e);
        }}"
      >
      </server-controls>`,
      root
    );
  }

  public openEditServerTags(server: ServerApiModel) {
    this.selectedServer = server;
    this.tagsDialog.open = true;
  }

  private editServer(e: CustomEvent) {
    this.selectedServer = e.detail.server;
    this.serverDialog.open = true;
  }
}
