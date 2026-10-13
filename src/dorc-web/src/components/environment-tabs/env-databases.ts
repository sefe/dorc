import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../add-edit-database.ts';
import '../attach-database';
import '../attached-databases';
import '../database-access-panel';
import { Notification } from '@vaadin/notification';
import { PageEnvBase } from './page-env-base';
import '@vaadin/dialog';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import {
  DatabaseApiModel,
  EnvironmentContentApiModel,
  RefDataEnvironmentsDetailsApi
} from '../../apis/dorc-api';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';
import GlobalCache from '../../global-cache';

@customElement('env-databases')
export class EnvDatabases extends PageEnvBase {
  @property({ type: Array })
  databases: Array<DatabaseApiModel> | undefined = [];

  @property({ type: Boolean }) envReadOnly = false;

  @state()
  private attachDatabaseDialogOpened = false;

  @state()
  private databaseAccessDialogOpened = false;

  @state()
  private databaseAccessId = 0;

  @state()
  private isAdmin = false;

  private get canManageDatabaseAccess() {
    return this.isAdmin || this.environment?.IsOwner === true;
  }

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        width: 100%;
        height: 100%;
        min-height: 0;
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
        font-size: var(--lumo-font-size-s);
        color: var(--dorc-link-color);
        padding: var(--lumo-space-xs);
      }
      vaadin-details {
        overflow: auto;
        width: calc(100% - 4px);
        flex: 1;
        min-height: 0;
        --divider-color: var(--dorc-border-color);
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Application Database Details"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; margin: 0px;"
      >
        <div>
          <div class="inline">
            <div class="inline">
              <vaadin-button
                title="Attach Database"
                @click="${this.openAttachDatabaseDialog}"
                .disabled="${!this.environment?.UserEditable}"
                >Attach Database</vaadin-button
              >
              <vaadin-dialog
                id="attach-database-dialog"
                header-title="Attach Database"
                .opened="${this.attachDatabaseDialogOpened}"
                draggable
                @opened-changed="${(event: DialogOpenedChangedEvent) => {
                  this.attachDatabaseDialogOpened = event.detail.value;
                }}"
                ${dialogRenderer(this.renderAttachDatabaseDialog, [this.databases, this.environmentId])}
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
              .canManageDatabaseAccess="${this.canManageDatabaseAccess}"
              @database-detached="${this._dbDetached}"
              @manage-database-access="${this.openDatabaseAccessDialog}"
            ></attached-databases>
            ${
              this.canManageDatabaseAccess
                ? html`
                    <vaadin-dialog
                      id="database-access-dialog"
                      header-title="${this.databaseAccessDialogTitle}"
                      resizable
                      draggable
                      width="min(72rem, calc(100vw - 4rem))"
                      .opened="${this.databaseAccessDialogOpened}"
                      @opened-changed="${(event: DialogOpenedChangedEvent) => {
                        this.databaseAccessDialogOpened = event.detail.value;
                        if (!event.detail.value) this.databaseAccessId = 0;
                      }}"
                      ${dialogRenderer(this.renderDatabaseAccessDialog, [
                        this.databases,
                        this.environmentId,
                        this.envReadOnly,
                        this.databaseAccessId
                      ])}
                      ${dialogFooterRenderer(
                        this.renderDatabaseAccessFooter,
                        []
                      )}
                    ></vaadin-dialog>
                  `
                : nothing
            }
          </div>
        </div>
      </vaadin-details>
    `;
  }

  constructor() {
    super();

    const gc = GlobalCache.getInstance();
    if (gc.userRoles === undefined) {
      gc.allRolesResp?.subscribe({
        next: (roles: string[]) => {
          this.isAdmin = roles.includes('Admin');
        },
        error: (err: string) => console.error(err)
      });
    } else {
      this.isAdmin = gc.userRoles.includes('Admin');
    }

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
    const api = new RefDataEnvironmentsDetailsApi(dorcApiConfiguration);
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
  };

  override notifyEnvironmentContentReady() {
    this.envReadOnly = !this.environment?.UserEditable;
    this.refreshDatabases();
  }

  sortDbs(a: DatabaseApiModel, b: DatabaseApiModel): number {
    if (String(a.Name).toLowerCase() > String(b.Name).toLowerCase()) return 1;
    if (a.Name?.toLowerCase() === b.Name?.toLowerCase()) {
      if (
        String(a.ServerName).toLowerCase() > String(b.ServerName).toLowerCase()
      )
        return 1;
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
    <div style="display: flex; justify-content: flex-end">
      <vaadin-button @click="${this.closeAttachDatabaseDialog}"
        >Close</vaadin-button
      >
    </div>
  `;

  private openAttachDatabaseDialog() {
    this.attachDatabaseDialogOpened = true;
  }

  private renderDatabaseAccessDialog = () => html`
    <database-access-panel
      style="width: 100%"
      .envId="${this.environmentId}"
      .databases="${this.databases ?? []}"
      .readonly="${this.envReadOnly}"
      .selectedDatabaseId="${this.databaseAccessId}"
    ></database-access-panel>
  `;

  private renderDatabaseAccessFooter = () => html`
    <vaadin-button @click="${this.closeDatabaseAccessDialog}"
      >Close</vaadin-button
    >
  `;

  private get databaseAccessDialogTitle() {
    const database = this.databases?.find(
      db => db.Id === this.databaseAccessId
    );
    return database
      ? `Database Access - ${database.ServerName} / ${database.Name}`
      : 'Database Access';
  }

  private openDatabaseAccessDialog(
    event: CustomEvent<{ database: DatabaseApiModel }>
  ) {
    this.databaseAccessId = event.detail.database.Id ?? 0;
    if (!this.databaseAccessId) return;
    this.databaseAccessDialogOpened = true;
  }

  private closeDatabaseAccessDialog() {
    this.databaseAccessDialogOpened = false;
    this.databaseAccessId = 0;
  }

  private closeAttachDatabaseDialog() {
    this.attachDatabaseDialogOpened = false;
  }
}
