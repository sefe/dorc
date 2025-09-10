import '@polymer/paper-toggle-button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../add-edit-database.ts';
import '../attach-database';
import '../attached-databases';
import { Notification } from '@vaadin/notification';
import { AttachedDatabases } from '../attached-databases';
import { PageEnvBase } from './page-env-base';
import { DatabaseApiModel, EnvironmentContentApiModel, RefDataEnvironmentsDetailsApi } from '../../apis/dorc-api';

@customElement('env-databases')
export class EnvDatabases extends PageEnvBase {
  @property({ type: Boolean }) addDatabase = false;

  @property({ type: Boolean }) attachDatabase = false;

  @property({ type: Array })
  databases: Array<DatabaseApiModel> | undefined = [];

  @property({ type: Boolean }) private envReadOnly = false;

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
              <paper-toggle-button
                class="buttons"
                id="addDatabase"
                .checked="${this.addDatabase}"
                @click="${this._addDatabase}"
                .disabled="${this.envReadOnly}"
                >ADD
              </paper-toggle-button>
            </div>
            <div class="inline">
              <paper-toggle-button
                class="buttons"
                id="attachDatabase"
                .checked="${this.attachDatabase}"
                @click="${this._attachDatabase}"
                .disabled="${this.envReadOnly}"
                >ATTACH
              </paper-toggle-button>
            </div>
          </div>
          ${this.addDatabase
            ? html` <div class="center-aligned">
                <add-edit-database
                  .envId="${this.environmentId}"
                  attach="true"
                  @database-attached="${this._dbAdded}"
                ></add-edit-database>
              </div>`
            : html``}
          ${this.attachDatabase
            ? html` <div class="center-aligned">
                <attach-database
                  .envId="${this.environmentId}"
                  .existingDatabases="${this.databases}"
                  @database-attached="${this._dbAttached}"
                ></attach-database>
              </div>`
            : html``}
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

  _addDatabase() {
    this.addDatabase = !this.addDatabase;
    if (this.addDatabase) {
      this.attachDatabase = !this.addDatabase;
    }
  }

  _attachDatabase() {
    this.attachDatabase = !this.attachDatabase;
    if (this.attachDatabase) {
      this.addDatabase = !this.attachDatabase;
    }
  }

  _dbAttached() {
    this.dbAttachSuccess('Database attached successfully');
    this.attachDatabase = false;
  }

  _dbDetached() {
    this.dbAttachSuccess('Database detached successfully');
    this.attachDatabase = false;
  }

  _dbAdded() {
    this.dbAttachSuccess('Database created & attached successfully');
    this.addDatabase = false;
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

  override notifyEnvironmentContentReady(loadedFromServer: boolean = false): void {
    this.envReadOnly = !this.environment?.UserEditable;
    if (!loadedFromServer) this.refreshDatabases();
    else {
      this.setDatabases(this.envContent);
    }
  }

  sortDbs(a: DatabaseApiModel, b: DatabaseApiModel): number {
    if (String(a.ServerName) > String(b.ServerName)) return 1;
    if (a.ServerName === b.ServerName) {
      if (String(a.Name) > String(b.Name)) return 1;
      return -1;
    }
    return -1;
  }
}
