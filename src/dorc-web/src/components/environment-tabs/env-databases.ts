import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, nothing, PropertyValues } from 'lit';
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
  DatabaseAccessApi,
  DatabaseApiModel,
  EnvironmentContentApiModel,
  RefDataDatabaseUsersApi,
  RefDataEnvironmentsDetailsApi
} from '../../apis/dorc-api';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';
import GlobalCache from '../../global-cache';
import { firstValueFrom } from 'rxjs';
import {
  unmigratedLegacyAccounts,
  type DatabaseAccessView
} from '../database-access-panel';

const databaseAccessTitles: Record<DatabaseAccessView, string> = {
  all: 'Database Access',
  migrate: 'Migrate database access',
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
  private unmigratedAccounts: Record<number, string[]> = {};

  @state()
  private isAdmin = false;

  private migrationCheckEpoch = 0;

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
              .unmigratedAccounts="${this.unmigratedAccounts}"
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
                        if (!event.detail.value) this.onDatabaseAccessClosed();
                      }}"
                      ${dialogRenderer(this.renderDatabaseAccessDialog, [
                        this.databases,
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
    this.onDatabaseAccessClosed();
  }

  private onDatabaseAccessClosed() {
    const database = this.databases?.find(
      db => db.Id === this.databaseAccessId
    );
    this.databaseAccessId = 0;
    if (database) void this.checkMigrations([database], false);
  }

  protected override updated(changed: PropertyValues) {
    super.updated?.(changed);
    if (
      changed.has('databases') ||
      changed.has('isAdmin') ||
      changed.has('environment') ||
      changed.has('environmentId')
    ) {
      const key = this.migrationCheckKey();
      if (key !== this.lastMigrationCheckKey) {
        this.lastMigrationCheckKey = key;
        void this.checkMigrations(this.databases ?? [], true);
      }
    }
  }

  private lastMigrationCheckKey = '';

  private migrationCheckKey() {
    if (
      !this.canManageDatabaseAccess ||
      !this.environmentId ||
      this.environmentId === -1
    )
      return '';
    return `${this.environmentId}:${(this.databases ?? [])
      .map(db => db.Id)
      .join(',')}`;
  }

  /**
   * Flags databases whose legacy permissions have not all been imported or
   * excluded. Databases are checked one at a time because each database
   * access read takes an exclusive per-database lock.
   */
  private async checkMigrations(
    databases: DatabaseApiModel[],
    replace: boolean
  ) {
    const epoch = replace
      ? ++this.migrationCheckEpoch
      : this.migrationCheckEpoch;
    if (replace) this.unmigratedAccounts = {};
    const envId = this.environmentId;
    if (!this.canManageDatabaseAccess || !envId || envId === -1) return;
    const api = new DatabaseAccessApi(dorcApiConfiguration);
    const legacyApi = new RefDataDatabaseUsersApi(dorcApiConfiguration);
    for (const database of databases) {
      const databaseId = database.Id;
      if (!databaseId || !database.ServerName || !database.Name) continue;
      let accounts: string[];
      try {
        const status = await firstValueFrom(
          api.databaseAccessStatus({ envId, databaseId })
        );
        if (!status.Enabled) return;
        const [state, assignments] = await Promise.all([
          firstValueFrom(api.databaseAccessGet({ envId, databaseId })),
          firstValueFrom(
            legacyApi.refDataDatabaseUsersGetDbUsersPermissionsGet({
              serverName: database.ServerName,
              databaseName: database.Name
            })
          )
        ]);
        accounts = unmigratedLegacyAccounts(assignments ?? [], state);
      } catch (err) {
        console.warn(
          `Could not check database access migration for ${database.Name}`,
          err
        );
        continue;
      }
      if (epoch !== this.migrationCheckEpoch || envId !== this.environmentId)
        return;
      const next = { ...this.unmigratedAccounts };
      if (accounts.length) next[databaseId] = accounts;
      else delete next[databaseId];
      this.unmigratedAccounts = next;
    }
  }

  private closeAttachDatabaseDialog() {
    this.attachDatabaseDialogOpened = false;
  }
}
