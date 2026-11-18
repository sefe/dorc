import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
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
import { DatabaseApiModel, RefDataDatabasesApi } from '../../apis/dorc-api';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';
import GlobalCache from '../../global-cache';
import type { DatabaseAccessView } from '../database-access-panel';

const databaseAccessTitles: Record<DatabaseAccessView, string> = {
  all: 'Database Access',
  permissions: 'Database permissions',
  audit: 'Database access audit'
};

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
  private databaseAccessView: DatabaseAccessView = 'all';

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
        display: flex;
        align-items: center;
        gap: var(--lumo-space-s);
      }
      .buttons {
        font-size: var(--lumo-font-size-s);
        color: var(--dorc-link-color);
        padding: var(--lumo-space-xs);
      }
      vaadin-details {
        overflow: hidden;
        width: calc(100% - 4px);
        flex: 1;
        min-height: 0;
        --divider-color: var(--dorc-border-color);
        display: flex;
        flex-direction: column;
      }
      vaadin-details::part(content) {
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
        overflow: hidden;
      }
      .details-content {
        display: flex;
        flex-direction: column;
        flex: 1;
        min-height: 0;
      }
      .toolbar {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-s);
        padding: var(--lumo-space-s) 0;
      }
      .databases-wrapper {
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
      }
      .summary-bar {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-s);
        font-weight: 400;
      }
      .summary-bar vaadin-icon {
        color: var(--dorc-link-color);
      }
      .count-badge {
        background-color: var(--dorc-link-color);
        color: var(--dorc-bg-primary);
        border-radius: 1em;
        font-size: var(--lumo-font-size-xs);
        font-weight: 600;
        padding: 0 var(--lumo-space-s);
        line-height: 1.6;
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; padding-right: 4px; margin: 0px; box-sizing: border-box;"
      >
        <vaadin-details-summary slot="summary">
          <div class="summary-bar">
            <vaadin-icon icon="vaadin:database" theme="small"></vaadin-icon>
            Application Database Details
            <span class="count-badge">${this.databases?.length ?? 0}</span>
          </div>
        </vaadin-details-summary>
        <div class="details-content">
          <div class="toolbar">
            <div class="inline">
              <vaadin-button
                title="Attach Database"
                @click="${this.openAttachDatabaseDialog}"
                .disabled="${!this.environment?.UserEditable}"
                ><vaadin-icon
                  icon="vaadin:link"
                  slot="prefix"
                ></vaadin-icon>
                Attach Database</vaadin-button
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
          <div class="databases-wrapper">
            <attached-databases
              id="attached-databases"
              .envId="${this.environmentId}"
              .databases="${this.databases}"
              .readonly="${this.envReadOnly}"
              .canManageDatabaseAccess="${this.canManageDatabaseAccess}"
              @database-detached="${this._dbDetached}"
              @manage-database-access="${this.openDatabaseAccessDialog}"
              @database-tags-updated="${this.refreshDatabases}"
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
                        this.environment,
                        this.environmentId,
                        this.envReadOnly,
                        this.databaseAccessId,
                        this.databaseAccessView
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
    // The base class assigns `environment` (which fires this hook) before it assigns
    // `environmentId` on the cold-cache path, so derive the id from the environment.
    const envId = this.environment?.EnvironmentId ?? this.environmentId;
    if (!envId || envId <= 0) return;
    new RefDataDatabasesApi(dorcApiConfiguration)
      .refDataDatabasesByEnvIdEnvIdGet({ envId })
      .subscribe({
        next: (data: DatabaseApiModel[]) => {
          this.databases = data.sort(this.sortDbs);
        },
        error: (err: any) => console.error(err)
      });
  }

  override notifyEnvironmentReady() {
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
      .envId="${this.environment?.EnvironmentId ?? this.environmentId}"
      .databases="${this.databases ?? []}"
      .readonly="${this.envReadOnly}"
      .selectedDatabaseId="${this.databaseAccessId}"
      .view="${this.databaseAccessView}"
    ></database-access-panel>
  `;

  private renderDatabaseAccessFooter = () => html`
    <vaadin-button @click="${this.closeDatabaseAccessDialog}"
      >Close</vaadin-button
    >
  `;

  private get databaseAccessDialogTitle() {
    const title = databaseAccessTitles[this.databaseAccessView];
    const database = this.databases?.find(
      db => db.Id === this.databaseAccessId
    );
    return database
      ? `${title} - ${database.ServerName} / ${database.Name}`
      : title;
  }

  private openDatabaseAccessDialog(
    event: CustomEvent<{
      database: DatabaseApiModel;
      view?: DatabaseAccessView;
    }>
  ) {
    this.databaseAccessId = event.detail.database.Id ?? 0;
    if (!this.databaseAccessId) return;
    this.databaseAccessView = event.detail.view ?? 'all';
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