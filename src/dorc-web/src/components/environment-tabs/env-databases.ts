import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../add-edit-database.ts';
import '../attach-database';
import '../attached-databases';
import { Notification } from '@vaadin/notification';
import { PageEnvBase } from './page-env-base';
import '@vaadin/dialog';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { DatabaseApiModel, EnvironmentContentApiModel, RefDataEnvironmentsDetailsApi } from '../../apis/dorc-api';

@customElement('env-databases')
export class EnvDatabases extends PageEnvBase {
  @property({ type: Array })
  databases: Array<DatabaseApiModel> | undefined = [];

  @property({ type: Boolean }) private envReadOnly = false;

  @state()
  private attachDatabaseDialogOpened = false;

  static get styles() {
    return css`
      :host {
        width: 100%;
      }
      .span {
        font-family: var(--lumo-font-family);
      }
      .center {
        margin: 10px 20px 10px;
        width: 100%;
        padding: 10px;
      }
      .inline {
        display: inline-block;
        vertical-align: middle;
      }
      .buttons {
        font-size: 10px;
        color: cornflowerblue;
        padding: 2px;
      }
      vaadin-details {
        overflow: auto;
        width: calc(100% - 4px);
        height: calc(100vh - 180px);
        --divider-color: rgb(223, 232, 239);
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Application Database Details"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px;"
      >
        <div>
          <div class="inline">
            <div class="inline">
              ${!this.envReadOnly
                ? html`
                  <vaadin-button
                    title="Attach Database"
                    theme="small"
                    @click="${this.openAttachDatabaseDialog}"
                  >Attach Database</vaadin-button>
                `
                : html``}
              <vaadin-dialog
                id='attach-database-dialog'
                header-title='Attach Database'
                .opened='${this.attachDatabaseDialogOpened}'
                draggable
                @opened-changed='${(event: DialogOpenedChangedEvent) => {
                  this.attachDatabaseDialogOpened = event.detail.value;
                }}'
                ${dialogRenderer(this.renderAttachDatabaseDialog, [])}
                ${dialogFooterRenderer(this.renderAttachDatabaseFooter, [])}
              ></vaadin-dialog>
            </div>
          </div>
          <div>
            <attached-databases
              id="attached-databases"
              .envId="${this.environmentId}"
              .databases="${this.databases}"
              .readonly="${this.envReadOnly}"
              @database-detached="${this._dbDetached}"
            ></attached-databases>
          </div>
        </div>
      </vaadin-details>
    `;
  }

  constructor() {
    super();

    super.loadEnvironmentInfo();
  }

  _dbAttached() {
    this.dbAttachSuccess('Database attached successfully');
    this.closeAttachDatabaseDialog();
  }

  _dbDetached() {
    this.dbAttachSuccess('Database detached successfully');
  }

  private dbAttachSuccess(text: string) {
    this.refreshDatabases();
    Notification.show(text, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }

  refreshDatabases() {
    if (!this.environmentId || this.environmentId === -1) return;
    const api = new RefDataEnvironmentsDetailsApi();
    api.refDataEnvironmentsDetailsIdGet({ id: this.environmentId }).subscribe(
      (data: EnvironmentContentApiModel) => {
        this.setDatabases(data);
      },
      (err: any) => console.error(err),
      () => console.log('done loading env details')
    );
  }

  setDatabases = (data: EnvironmentContentApiModel | undefined) => {
    console.log(
      `Setting Databases on env-databases page ${this.environment?.EnvironmentName}`
    );

    this.databases =
      data?.DbServers !== null
        ? data?.DbServers?.sort(this.sortDbs)
        : undefined;
  }

  override notifyEnvironmentContentReady() {
    // this.envReadOnly = !this.environment?.UserEditable;
    this.envReadOnly = false; // Temporarily allow edits until permissions are sorted out
    this.refreshDatabases();
  }

  sortDbs(a: DatabaseApiModel, b: DatabaseApiModel): number {
    if (String(a.ServerName) > String(b.ServerName)) return 1;
    if (a.ServerName === b.ServerName) {
      if (String(a.Name) > String(b.Name)) return 1;
      return -1;
    }
    return -1;
  }
    private renderAttachDatabaseDialog = () => html`
    <attach-database
      id="attach-database"
      .envId="${this.environmentId}"
      .existingDatabases="${this.databases}"
      @database-attached="${this._dbAttached}"
    ></attach-database>
  `;

  private renderAttachDatabaseFooter = () => html`
    <vaadin-button @click="${this.closeAttachDatabaseDialog}"
      >Close</vaadin-button
    >
  `;

  private openAttachDatabaseDialog() {
    this.attachDatabaseDialogOpened = true;
  }

  private closeAttachDatabaseDialog() {
    this.attachDatabaseDialogOpened = false;
  }

}

