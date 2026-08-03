import { columnBodyRenderer } from '@vaadin/grid/lit';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/button';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/dialog';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogRenderer } from '@vaadin/dialog/lit';
import { css, LitElement } from 'lit';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import './grid-button-groups/database-env-controls.ts';
import './grid-button-groups/server-controls';
import '../components/server-tags';
import './server-tags';
import '../components/add-edit-server';
import './add-edit-server';
import '../components/map-daemons.ts';
import { Notification } from '@vaadin/notification';
import { map } from 'lit/directives/map.js';
import { ServerApiModel } from '../apis/dorc-api';
import type { EnvironmentContentApiModel } from '../apis/dorc-api';
import { splitTags } from '../helpers/tag-parser';

@customElement('attached-servers')
export class AttachedServers extends ResponsiveMixin(LitElement) {
  @property({ type: Object })
  envContent!: EnvironmentContentApiModel;

  @property({ type: Array })
  servers: Array<ServerApiModel> | undefined = [];

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) private readonly = true;

  @property({ type: Object })
  selectedServer: ServerApiModel | undefined;

  @state() private serverDialogOpened = false;

  @state() private tagsDialogOpened = false;

  @state() private daemonMappingDialogOpened = false;

  private renderAddEditServer = () => html`
    <add-edit-server
      id="add-edit-server"
      .srv="${this.selectedServer}"
      @server-updated="${this.serverUpdated}"
    ></add-edit-server>
  `;

  private renderServerTags = () => html`
    <server-tags
      id="tags"
      .server="${this.selectedServer}"
      @server-tags-updated="${this.serverTagsUpdated}"
    ></server-tags>
  `;

  private renderMapDaemons = () => html`
    <map-daemons
      .server="${this.selectedServer}"
      .readonly="${this.readonly}"
    ></map-daemons>
  `;

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
      :host {
        display: flex;
        flex-direction: column;
        flex: 1;
        min-height: 0;
        height: 100%;
      }
      vaadin-grid#grid {
        flex: 1;
        min-height: 0;
        width: calc(100% - 4px);
        --divider-color: var(--dorc-border-color);
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
      .tag {
        font-size: var(--lumo-font-size-s);
        font-family: monospace;
        background-color: var(--dorc-chip-bg);
        color: var(--dorc-chip-text);
        display: inline-block;
        padding: 3px;
        margin: 3px;
        text-decoration: none;
        border-radius: 3px;
      }
    .column-content {
      display: block;
      width: 100%;
    }
      vaadin-grid-cell-content {
        white-space: normal;
        word-wrap: break-word;
        overflow-wrap: break-word;
      }
    `;
  }

  render() {
    return html`
      <vaadin-dialog
        id="add-edit-server-dialog"
        header-title="Add/Edit Server"
        draggable
        .opened="${this.serverDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.serverDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderAddEditServer, [this.selectedServer])}
      ></vaadin-dialog>

      <vaadin-dialog
        id="tags-dialog"
        header-title="Edit Server Tags for ${this.selectedServer?.Name}"
        draggable
        .opened="${this.tagsDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.tagsDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderServerTags, [this.selectedServer])}
      ></vaadin-dialog>

      <vaadin-dialog
        id="daemon-mapping-dialog"
        header-title="Manage Daemon Mappings for ${this.selectedServer?.Name}"
        draggable
        .opened="${this.daemonMappingDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.daemonMappingDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderMapDaemons, [
          this.selectedServer,
          this.readonly
        ])}
      ></vaadin-dialog>
      
      <vaadin-grid
        id="grid"
        .items=${this.servers}
        theme="compact row-stripes no-row-borders no-border"
        multi-sort
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
          ?hidden="${this._narrowScreen}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          ${columnBodyRenderer(this.applicationTagsRenderer, [])}
          header="Application Tags"
          resizable
          ?hidden="${this._narrowScreen}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          width="200px"
          flex-grow="0"
          ${columnBodyRenderer(this._boundServersButtonsRenderer, [])}
        >
        </vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  private applicationTagsRenderer = (
    item: ServerApiModel
  ) => {
    const server = item;
    const appTags = splitTags(server.ApplicationTags);

    return html`
        ${map(
          appTags,
          value =>
            html`<button style="border: 0px" class="tag">${value}</button>`
        )}
      `;
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
    this.serverDialogOpened = false;
  }

  serverTagsUpdated() {
    this.dispatchEvent(
      new CustomEvent('environment-stale', {
        bubbles: true,
        composed: true,
        detail: {}
      })
    );
    this.tagsDialogOpened = false;
  }

  _boundServersButtonsRenderer(
    item: AttachedServers
  ) {
    const server = item as ServerApiModel;
    return html` <server-controls
        .envId="${this.envId}"
        .envSet="${true}"
        .serverDetails="${server}"
        .readonly="${this.readonly}"
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
          this.openEditServerTags(server);
        }}"
        @edit-server="${(e: CustomEvent) => {
          this.editServer(e);
        }}"
        @map-daemons="${() => {
          this.openDaemonMapping(server);
        }}"
      >
      </server-controls>`;
  }

  public openEditServerTags(server: ServerApiModel) {
    this.selectedServer = server;
    this.tagsDialogOpened = true;
  }

  public openDaemonMapping(server: ServerApiModel) {
    this.selectedServer = server;
    this.daemonMappingDialogOpened = true;
  }

  private editServer(e: CustomEvent) {
    this.selectedServer = e.detail.server;
    this.serverDialogOpened = true;
  }
}