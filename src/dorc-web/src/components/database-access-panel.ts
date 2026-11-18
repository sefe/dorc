import { css, html, LitElement, nothing, PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { firstValueFrom, Observable } from 'rxjs';
import { AjaxError } from 'rxjs/ajax';
import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/checkbox';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import '@vaadin/radio-group';
import '@vaadin/icon';
import '@vaadin/icons';
import '@vaadin/tooltip';
import type { ComboBoxValueChangedEvent } from '@vaadin/combo-box';
import type { TextFieldValueChangedEvent } from '@vaadin/text-field';
import type { CheckboxCheckedChangedEvent } from '@vaadin/checkbox';
import type { DetailsOpenedChangedEvent } from '@vaadin/details';
import type { RadioGroupValueChangedEvent } from '@vaadin/radio-group';
import {
  DatabaseAccessApi,
  DatabaseAccessAuditApiModel,
  DatabaseAccessDirectoryIdentity,
  DatabaseAccessPrincipal,
  DatabaseAccessState,
  DatabaseAccessStatus,
  DatabaseApiModel
} from '../apis/dorc-api';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { confirmPrompt } from './confirm-prompt';

export function databaseAccessError(error: unknown): string {
  if (error instanceof AjaxError) {
    const response: unknown = error.response;
    if (
      response &&
      typeof response === 'object' &&
      'detail' in response &&
      typeof response.detail === 'string'
    )
      return response.detail;
    return `Request failed (${error.status}). Reload and check your access or the service logs.`;
  }
  return error instanceof Error
    ? error.message
    : 'Database access request failed.';
}

export type DatabaseAccessView = 'all' | 'permissions' | 'audit';

type GrantKind = 'directory' | 'sql';

/** Fixed database roles that ordinary editing may grant; db_owner requires owner approval across all attached environments and the others are protected. */
export const grantableFixedRoles = [
  'db_datareader',
  'db_datawriter',
  'db_ddladmin',
  'db_backupoperator',
  'db_denydatareader',
  'db_denydatawriter'
];
const protectedRoles = [
  'public',
  'db_owner',
  'db_securityadmin',
  'db_accessadmin'
];
const sameName = (a: string | null | undefined, b: string) =>
  (a ?? '').toLowerCase() === b.toLowerCase();
const isDbOwner = (role: string) => sameName(role, 'db_owner');

@customElement('database-access-panel')
export class DatabaseAccessPanel extends LitElement {
  @property({ type: Number }) envId = 0;
  @property({ type: Array }) databases: DatabaseApiModel[] = [];
  @property({ type: Boolean }) readonly = true;
  @property({ type: Number }) selectedDatabaseId = 0;
  @property({ type: String }) view: DatabaseAccessView = 'all';
  @state() private databaseId = 0;
  @state() private status?: DatabaseAccessStatus;
  @state() private desired?: DatabaseAccessState;
  @state() private audit: DatabaseAccessAuditApiModel[] = [];
  @state() private identities: DatabaseAccessDirectoryIdentity[] = [];
  @state() private directoryNames: Record<string, string> = {};
  @state() private busy = false;
  @state() private error = '';
  @state() private message = '';
  private epoch = 0;
  private readonly api = new DatabaseAccessApi(dorcApiConfiguration);
  @state() private grantKind: GrantKind = 'directory';
  @state() private grantSearch = '';
  @state() private grantIdentityId = '';
  @state() private grantLogin = '';
  @state() private grantUserName = '';
  @state() private grantUserNameEdited = false;
  @state() private grantRoles: string[] = [];
  @state() private grantEditing = '';
  @state() private grantAdvancedOpen = false;
  @state() private grantError = '';
  @state() private sqlLogins?: string[];
  @state() private sqlLoginsMessage = '';
  @state() private customRoleName = '';
  @state() private customRoleError = '';

  static styles = css`
    :host {
      display: block;
      min-width: 0;
      box-sizing: border-box;
      font-family: var(--lumo-font-family);
      font-size: var(--lumo-font-size-m);
      color: var(--dorc-text-primary);
    }
    vaadin-details {
      margin: 0;
      min-width: 0;
      max-width: 100%;
      --divider-color: var(--dorc-border-color);
    }
    vaadin-details-summary::part(content) {
      min-width: 0;
      white-space: normal;
      overflow-wrap: anywhere;
    }
    .panel {
      border-top: 1px solid var(--dorc-border-color);
      margin-top: var(--lumo-space-s);
    }
    .content {
      padding: 0 var(--lumo-space-s) var(--lumo-space-s);
      min-width: 0;
    }
    .workflow-step {
      min-width: 0;
      margin-top: var(--lumo-space-m);
      padding-top: var(--lumo-space-s);
      border-top: 1px solid var(--dorc-border-color);
    }
    .workflow-step h3 {
      margin: var(--lumo-space-s) 0;
      font-size: var(--lumo-font-size-m);
    }
    .toolbar,
    fieldset {
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-s);
      align-items: end;
    }
    .toolbar {
      margin-block: var(--lumo-space-s);
    }
    vaadin-text-field,
    vaadin-combo-box,
    vaadin-text-area {
      width: 18rem;
      max-width: 100%;
      min-width: 0;
      --vaadin-input-field-label-color: var(--dorc-text-secondary-strong);
    }
    vaadin-combo-box::part(toggle-button) {
      color: var(--dorc-text-secondary-strong);
    }
    #database {
      width: 32rem;
    }
    vaadin-button {
      flex-shrink: 0;
      max-width: 100%;
    }
    vaadin-checkbox {
      align-self: center;
    }
    p {
      margin: var(--lumo-space-s) 0;
    }
    .description,
    .metadata,
    legend,
    caption {
      color: var(--dorc-text-secondary);
      font-size: var(--lumo-font-size-s);
    }
    .notice {
      padding: var(--lumo-space-s) var(--lumo-space-m);
      border-left: 3px solid var(--dorc-link-color);
      background: var(--dorc-bg-secondary);
      overflow-wrap: anywhere;
    }
    .notice p:first-child {
      margin-top: 0;
    }
    .notice p:last-child {
      margin-bottom: 0;
    }
    fieldset {
      min-width: 0;
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-m);
      padding: var(--lumo-space-s);
      margin: var(--lumo-space-m) 0;
    }
    caption {
      text-align: left;
      padding: var(--lumo-space-s);
    }
    table {
      width: 100%;
      min-width: 36rem;
      border-collapse: collapse;
    }
    th,
    td {
      text-align: left;
      padding: var(--lumo-space-s);
      border-bottom: 1px solid var(--dorc-border-color);
      overflow-wrap: anywhere;
    }
    .scroll {
      overflow-x: auto;
      max-width: 100%;
    }
    #grant-access {
      flex-direction: column;
      align-items: stretch;
    }
    .grant-row,
    .role-options {
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-s);
      align-items: end;
    }
    .role-options {
      align-items: center;
    }
    .role-options > span {
      flex-basis: 100%;
    }
    #grant-advanced vaadin-text-field {
      width: 28rem;
    }
    .role-chip {
      display: inline-flex;
      align-items: center;
      margin: 0 var(--lumo-space-xs) var(--lumo-space-xs) 0;
      padding-left: var(--lumo-space-s);
      border-radius: var(--lumo-border-radius-m);
      background: var(--dorc-bg-secondary);
      white-space: nowrap;
    }
    .role-chip vaadin-button {
      margin: 0;
      min-width: 0;
    }
    td.row-actions {
      white-space: nowrap;
      width: 1%;
    }
    [role='alert'] {
      color: var(--dorc-error-color);
      white-space: pre-wrap;
    }
    pre {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
  `;

  protected willUpdate(changed: PropertyValues) {
    if (
      (changed.has('selectedDatabaseId') ||
        changed.has('envId') ||
        changed.has('databases')) &&
      this.selectedDatabaseId &&
      this.databases.some(db => db.Id === this.selectedDatabaseId)
    ) {
      if (this.databaseId !== this.selectedDatabaseId) {
        this.databaseId = this.selectedDatabaseId;
        this.reset();
      }
      return;
    }
    if (
      changed.has('envId') ||
      (changed.has('databases') &&
        !this.databases.some(db => db.Id === this.databaseId))
    ) {
      this.databaseId = 0;
      this.reset();
    }
  }

  protected updated(changed: PropertyValues) {
    if (
      (changed.has('selectedDatabaseId') ||
        changed.has('envId') ||
        changed.has('databases')) &&
      this.selectedDatabaseId &&
      this.databaseId === this.selectedDatabaseId &&
      this.databases.some(db => db.Id === this.selectedDatabaseId)
    ) {
      const selectedDatabaseId = this.selectedDatabaseId;
      queueMicrotask(async () => {
        if (this.isConnected && this.databaseId === selectedDatabaseId) {
          await this.load();
        }
      });
    }
  }

  private get canManageDbOwner() {
    return !!this.status?.CanApproveDbOwner;
  }

  disconnectedCallback() {
    this.epoch++;
    super.disconnectedCallback();
  }

  private reset() {
    this.epoch++;
    this.status = undefined;
    this.desired = undefined;
    this.audit = [];
    this.identities = [];
    this.directoryNames = {};
    this.resetGrant();
    this.sqlLogins = undefined;
    this.sqlLoginsMessage = '';
    this.customRoleName = '';
    this.customRoleError = '';
    this.busy = false;
    this.error = '';
    this.message = '';
  }

  private get target() {
    return { envId: this.envId, databaseId: this.databaseId };
  }

  private async request<T>(
    source: Observable<T>,
    accept: (value: T) => void
  ) {
    const epoch = this.epoch;
    this.busy = true;
    this.error = '';
    this.message = '';
    try {
      const value = await firstValueFrom(source);
      if (epoch === this.epoch) {
        accept(value);
        return true;
      }
      return false;
    } catch (error) {
      console.error('Database access operation failed', error);
      if (epoch === this.epoch) this.error = databaseAccessError(error);
      return false;
    } finally {
      if (epoch === this.epoch) this.busy = false;
    }
  }

  private async load() {
    this.reset();
    const target = this.target;
    const epoch = this.epoch;
    await this.request(this.api.databaseAccessStatus(target), value => {
      this.status = value;
    });
    if (epoch !== this.epoch || !this.status?.Enabled) return;
    await this.request(this.api.databaseAccessGet(target), value => {
      this.desired = value;
    });
    if (epoch !== this.epoch || !this.desired) return;
    await this.request(this.api.databaseAccessAudit(target), value => {
      this.audit = value;
    });
    for (const principal of this.desired.Principals) {
      if (!principal.DirectoryId || epoch !== this.epoch) continue;
      const id = principal.DirectoryId;
      try {
        const identity = await firstValueFrom(
          this.api.databaseAccessResolve({ ...target, id })
        );
        if (epoch === this.epoch)
          this.directoryNames = {
            ...this.directoryNames,
            [id]: identity.DisplayName
          };
      } catch (error) {
        console.error('Directory reference could not be resolved', error);
        if (epoch === this.epoch)
          this.directoryNames = {
            ...this.directoryNames,
            [id]: `Unresolved: ${databaseAccessError(error)}`
          };
      }
    }
  }

  private save(change: (state: DatabaseAccessState) => void) {
    if (!this.desired || this.busy || this.readonly)
      return Promise.resolve(false);
    const state = structuredClone(this.desired);
    change(state);
    return this.request(
      this.api.databaseAccessSave({
        ...this.target,
        databaseAccessState: state
      }),
      value => {
        this.desired = value;
        this.message =
          'Desired state saved. No target database changes have been applied.';
      }
    );
  }

  private resetGrant() {
    this.grantKind = 'directory';
    this.grantSearch = '';
    this.identities = [];
    this.grantIdentityId = '';
    this.grantLogin = '';
    this.grantUserName = '';
    this.grantUserNameEdited = false;
    this.grantRoles = [];
    this.grantEditing = '';
    this.grantAdvancedOpen = false;
    this.grantError = '';
  }

  /** The principal that already represents the chosen account, if any. */
  private grantExisting(
    desired: DatabaseAccessState
  ): DatabaseAccessPrincipal | undefined {
    const principals = desired.Principals;
    if (this.grantEditing)
      return principals.find(p => p.Name === this.grantEditing);
    if (this.grantKind === 'directory')
      return this.grantIdentityId
        ? principals.find(
            p => p.Kind !== 'Native' && p.DirectoryId === this.grantIdentityId
          )
        : undefined;
    const login = this.grantLogin.trim();
    return login
      ? principals.find(
          p => p.Kind === 'Native' && sameName(p.LoginName, login)
        )
      : undefined;
  }

  private presentRoles(desired: DatabaseAccessState, principal: string) {
    return desired.Memberships.filter(
      m => m.Present && m.Principal === principal
    ).map(m => m.Role);
  }

  private grantAccountChanged(desired: DatabaseAccessState) {
    this.grantError = '';
    const existing = this.grantExisting(desired);
    if (existing) {
      this.grantUserName = existing.Name;
      this.grantUserNameEdited = false;
      this.grantRoles = this.presentRoles(desired, existing.Name).filter(
        r => this.canManageDbOwner || !isDbOwner(r)
      );
      return;
    }
    if (this.grantUserNameEdited) return;
    this.grantUserName =
      this.grantKind === 'directory'
        ? (this.identities.find(i => i.Id === this.grantIdentityId)
            ?.AccountName ?? '')
        : this.grantLogin.trim();
    if (
      this.grantKind === 'directory' &&
      this.grantIdentityId &&
      !this.grantUserName
    )
      this.grantAdvancedOpen = true;
  }

  private grantRoleOptions(desired: DatabaseAccessState) {
    const names = [...grantableFixedRoles];
    if (this.canManageDbOwner) names.push('db_owner');
    for (const role of desired.Roles)
      if (
        role.Present &&
        !protectedRoles.some(p => sameName(p, role.Name)) &&
        !names.some(n => sameName(n, role.Name))
      )
        names.push(role.Name);
    return names;
  }

  private searchDirectory(desired: DatabaseAccessState) {
    const search = this.grantSearch.trim();
    if (search.length < 3) {
      this.grantError = 'Enter at least 3 characters to search AD.';
      return;
    }
    return this.request(
      this.api.databaseAccessDirectory({ ...this.target, search }),
      value => {
        this.identities = value;
        this.grantIdentityId = value.length === 1 ? value[0].Id : '';
        this.grantAccountChanged(desired);
        if (!value.length)
          this.grantError = `No AD users or groups match '${search}'.`;
      }
    );
  }

  private async loadSqlLogins() {
    if (this.sqlLogins) return;
    const epoch = this.epoch;
    this.sqlLoginsMessage = 'Loading SQL logins from the server…';
    try {
      const logins = await firstValueFrom(
        this.api.databaseAccessSqlLogins(this.target)
      );
      if (epoch !== this.epoch) return;
      this.sqlLogins = logins;
      this.sqlLoginsMessage = logins.length
        ? ''
        : 'No enabled SQL logins were found on this server. A DBA must create the login before access can be granted.';
    } catch (error) {
      console.error('SQL logins could not be listed', error);
      if (epoch !== this.epoch) return;
      this.sqlLogins = [];
      this.sqlLoginsMessage = `Could not list the server's SQL logins (${databaseAccessError(error)}). You can still type the name of an existing login.`;
    }
  }

  private async editAccess(
    desired: DatabaseAccessState,
    principal: DatabaseAccessPrincipal
  ) {
    this.resetGrant();
    this.grantEditing = principal.Name;
    this.grantKind = principal.Kind === 'Native' ? 'sql' : 'directory';
    this.grantLogin = principal.LoginName ?? '';
    if (principal.DirectoryId) {
      this.grantIdentityId = principal.DirectoryId;
      this.identities = [
        {
          Id: principal.DirectoryId,
          DisplayName:
            this.directoryNames[principal.DirectoryId] ?? principal.Name,
          IsGroup: principal.Kind === 'DirectoryGroup',
          AccountName: principal.Name,
          Status: 'Resolved'
        }
      ];
    }
    this.grantUserName = principal.Name;
    this.grantRoles = this.presentRoles(desired, principal.Name).filter(
      r => this.canManageDbOwner || !isDbOwner(r)
    );
    await this.updateComplete;
    (this.renderRoot.querySelector('#grant-access') as HTMLElement).scrollIntoView(
      { block: 'nearest' }
    );
  }

  private isCurrent(epoch: number) {
    return this.isConnected && epoch === this.epoch;
  }

  private async saveGrant(desired: DatabaseAccessState) {
    const existing = this.grantExisting(desired);
    const identity = this.identities.find(i => i.Id === this.grantIdentityId);
    const login = this.grantLogin.trim();
    const name = existing?.Name ?? this.grantUserName.trim();
    const fail = (message: string, advanced = false) => {
      this.grantError = message;
      if (advanced) this.grantAdvancedOpen = true;
    };
    if (!existing) {
      if (this.grantKind === 'directory' && !identity)
        return fail('Search for and choose an AD user or group.');
      if (this.grantKind === 'sql' && !login)
        return fail('Choose an existing SQL login.');
      if (!name)
        return fail(
          'Enter a database user name under Advanced. It could not be filled in automatically for this account.',
          true
        );
      if (name.length > 128 || [...name].some(c => c.charCodeAt(0) < 32))
        return fail(
          'Database user names must be 1-128 characters with no control characters.',
          true
        );
      if (desired.Principals.some(p => sameName(p.Name, name)))
        return fail(
          `A database user named '${name}' already exists for a different account. Choose another name under Advanced.`,
          true
        );
    }
    const grantKind = this.grantKind;
    const roles = [...this.grantRoles];
    const canManageDbOwner = this.canManageDbOwner;
    const hadDbOwner = this.presentRoles(desired, name).some(isDbOwner);
    const epoch = this.epoch;
    if (roles.some(isDbOwner) && !hadDbOwner) {
      this.busy = true;
      const confirmed = await confirmPrompt(
        `Grant db_owner membership to '${name}'? This grants full database control when the deployment applies it to the target.`
      );
      if (!this.isCurrent(epoch)) return;
      this.busy = false;
      if (!confirmed) return;
    }
    const saved = await this.save(state => {
      const principal = state.Principals.find(p => p.Name === name);
      if (principal) principal.Present = true;
      else
        state.Principals.push(
          grantKind === 'directory'
            ? {
                Name: name,
                Kind: identity!.IsGroup ? 'DirectoryGroup' : 'DirectoryUser',
                LoginName: null,
                DirectoryId: identity!.Id,
                Present: true
              }
            : {
                Name: name,
                Kind: 'Native',
                LoginName: login,
                DirectoryId: null,
                Present: true
              }
        );
      const granted = roles.map(role => {
        const existingRole = state.Roles.find(r => sameName(r.Name, role));
        if (!existingRole) {
          state.Roles.push({ Name: role, Managed: false, Present: true });
          return role;
        }
        existingRole.Present = true;
        return existingRole.Name;
      });
      for (const membership of state.Memberships)
        if (
          membership.Principal === name &&
          (canManageDbOwner || !isDbOwner(membership.Role))
        )
          membership.Present = granted.some(r => sameName(r, membership.Role));
      for (const role of granted)
        if (
          !state.Memberships.some(
            m => m.Principal === name && sameName(m.Role, role)
          )
        )
          state.Memberships.push({
            Principal: name,
            Role: role,
            Present: true
          });
    });
    if (saved) {
      this.resetGrant();
      this.message = `Saved access for ${name}. No target database changes have been applied.`;
    }
  }

  private saveCustomRole(desired: DatabaseAccessState) {
    const name = this.customRoleName.trim();
    const existing = desired.Roles.find(r => sameName(r.Name, name));
    this.customRoleError = !name
      ? 'Enter a role name.'
      : name.length > 128
        ? 'Role names must be 1-128 characters.'
        : name.toLowerCase().startsWith('db_')
          ? "Custom role names cannot start with 'db_'; that prefix is reserved for SQL Server's fixed roles."
          : existing
            ? `A role named '${existing.Name}' is already configured.`
            : '';
    if (this.customRoleError) return;
    return this.save(state => {
      state.Roles.push({ Name: name, Managed: true, Present: true });
    }).then(saved => {
      if (saved) this.customRoleName = '';
    });
  }

  render() {
    const disabled = this.busy || this.readonly;
    const selectedDatabase = this.databases.find(
      database => database.Id === this.selectedDatabaseId
    );
    return html`
      <div class="panel">
        <div class="content">
          <p class="description">
            ${
              {
                all: 'Manage SQL database users and roles for this environment.',
                permissions:
                  'Assign database users, roles and memberships for this database.',
                audit:
                  'Recent changes and reconciliation outcomes for this database.'
              }[this.view]
            }
            Saving changes updates the configuration in DOrc only; it does not
            connect to or change the target database.
          </p>
          <div class="toolbar">
            ${
              selectedDatabase
                ? html`<span id="selected-database" class="metadata">
                    Database:
                    ${`${selectedDatabase.ServerName} / ${selectedDatabase.Name}`}
                  </span>`
                : html`
                    <vaadin-combo-box
                      id="database"
                      label="Database"
                      placeholder="Select a database"
                      item-label-path="label"
                      item-value-path="value"
                      .items=${this.databases.map(db => ({
                        label: `${db.ServerName} / ${db.Name}`,
                        value: String(db.Id ?? 0)
                      }))}
                      .value=${this.databaseId ? String(this.databaseId) : ''}
                      ?disabled=${this.busy}
                      @value-changed=${(e: ComboBoxValueChangedEvent) => {
                        const id = Number(e.detail.value);
                        if (id === this.databaseId) return;
                        this.databaseId = id;
                        this.reset();
                      }}
                    ></vaadin-combo-box>
                  `
            }
            <vaadin-button
              ?disabled=${this.busy || !this.databaseId}
              @click=${this.load}
              >${
                selectedDatabase
                  ? 'Reload database access'
                  : 'Load database access'
              }</vaadin-button
            >
          </div>
          <div role="status" aria-live="polite">
            ${this.busy || this.message ? html`<p>${this.busy ? 'Working…' : this.message}</p>` : nothing}
          </div>
          ${this.error ? html`<p role="alert">${this.error}</p>` : nothing}
          ${
            this.status && !this.status.Enabled
              ? html` <div class="notice" role="status">
                  <p>Database access management is disabled.</p>
                  <p class="description">
                    A DOrc administrator must enable
                    <code>DatabaseAccess:Enabled</code> in the API
                    configuration.
                  </p>
                </div>`
              : nothing
          }
          ${
            this.status && this.desired
              ? html`
                  <p class="metadata">
                    Provider: ${this.desired.Provider} · revision
                    ${this.desired.Revision} · ${this.desired.Principals.length}
                    principals
                  </p>
                  <p class="description">
                    ${this.status.Limitations.join(' ')}
                  </p>
                  ${
                    !this.status.Providers.includes(this.desired.Provider)
                      ? html`<div class="notice" role="status">
                            <p>
                              Database access management is not yet supported
                              for provider <code>${this.desired.Provider}</code>.
                            </p>
                            <p class="description">
                              Supported providers:
                              ${this.status.Providers.join(', ') || 'none'}.
                            </p>
                          </div>
                          ${this.view === 'audit' ? this.renderAudit() : nothing}`
                      : this.view === 'all'
                        ? html`<section class="workflow-step">
                              ${this.renderDesiredStateEditor(
                                this.desired,
                                disabled
                              )}
                            </section>
                            ${this.renderAudit()}`
                        : this.view === 'permissions'
                          ? this.renderDesiredStateEditor(
                              this.desired,
                              disabled
                            )
                          : this.renderAudit()
                  }
                `
              : nothing
          }
        </div>
      </div>
    `;
  }

  private renderDesiredStateEditor(
    desired: DatabaseAccessState,
    disabled: boolean
  ) {
    const existing = this.grantExisting(desired);
    const roleOptions = this.grantRoleOptions(desired);
    const customRoles = desired.Roles.filter(r => r.Managed);
    const account = (p: DatabaseAccessPrincipal) =>
      p.Kind === 'Native'
        ? `SQL login ${p.LoginName ?? ''}`
        : `${p.DirectoryId ? (this.directoryNames[p.DirectoryId] ?? p.DirectoryId) : ''} (AD ${p.Kind === 'DirectoryGroup' ? 'group' : 'user'})`;
    const content = html`
      <p class="description">
        Changes are saved to DOrc only; they do not change SQL permissions on
        the target database.
      </p>
      <fieldset id="grant-access" ?disabled=${disabled}>
        <legend>
          ${this.grantEditing ? `Edit access for ${this.grantEditing}` : 'Grant access'}
        </legend>
        <vaadin-radio-group
          label="Account type"
          .value=${this.grantKind}
          ?disabled=${disabled || !!this.grantEditing}
          @value-changed=${(e: RadioGroupValueChangedEvent) => {
            const kind = e.detail.value as GrantKind;
            if (!kind || kind === this.grantKind) return;
            this.grantKind = kind;
            this.grantIdentityId = '';
            this.grantLogin = '';
            this.grantUserName = '';
            this.grantUserNameEdited = false;
            this.grantError = '';
            if (kind === 'sql') void this.loadSqlLogins();
          }}
        >
          <vaadin-radio-button
            value="directory"
            label="AD user or group"
          ></vaadin-radio-button>
          <vaadin-radio-button
            value="sql"
            label="SQL login"
          ></vaadin-radio-button>
        </vaadin-radio-group>
        <div class="grant-row">
          ${
            this.grantKind === 'directory'
              ? html`
                  ${
                    this.grantEditing
                      ? nothing
                      : html`<vaadin-text-field
                            id="grant-search"
                            label="Search AD"
                            placeholder="Name or account, 3+ characters"
                            ?disabled=${disabled}
                            .value=${this.grantSearch}
                            @value-changed=${(
                              e: TextFieldValueChangedEvent
                            ) => {
                              this.grantSearch = e.detail.value;
                            }}
                            @keydown=${(e: KeyboardEvent) => {
                              if (e.key === 'Enter')
                                void this.searchDirectory(desired);
                            }}
                          ></vaadin-text-field>
                          <vaadin-button
                            ?disabled=${disabled}
                            @click=${() => this.searchDirectory(desired)}
                            >Search</vaadin-button
                          >`
                  }
                  <vaadin-combo-box
                    id="grant-identity"
                    label="User or group"
                    placeholder=${this.identities.length ? 'Choose a result' : 'Search AD first'}
                    ?disabled=${disabled || !!this.grantEditing || !this.identities.length}
                    item-label-path="label"
                    item-value-path="value"
                    .items=${this.identities.map(i => ({
                      label: `${i.DisplayName}${i.AccountName ? ` (${i.AccountName})` : ''} · ${i.IsGroup ? 'group' : 'user'}`,
                      value: i.Id
                    }))}
                    .value=${this.grantIdentityId}
                    @value-changed=${(e: ComboBoxValueChangedEvent) => {
                      if (e.detail.value === this.grantIdentityId) return;
                      this.grantIdentityId = e.detail.value;
                      this.grantAccountChanged(desired);
                    }}
                  ></vaadin-combo-box>
                `
              : html`<vaadin-combo-box
                  id="grant-login"
                  label="SQL login"
                  placeholder="Choose an existing login"
                  allow-custom-value
                  helper-text="Logins are created by a DBA; DOrc only maps them to database users."
                  ?disabled=${disabled || !!this.grantEditing}
                  .items=${this.sqlLogins ?? []}
                  .value=${this.grantLogin}
                  @value-changed=${(e: ComboBoxValueChangedEvent) => {
                    if (e.detail.value === this.grantLogin) return;
                    this.grantLogin = e.detail.value;
                    this.grantAccountChanged(desired);
                  }}
                ></vaadin-combo-box>`
          }
        </div>
        ${
          this.grantKind === 'sql' && this.sqlLoginsMessage
            ? html`<p class="metadata" id="sql-logins-message">
                ${this.sqlLoginsMessage}
              </p>`
            : nothing
        }
        ${
          existing && !this.grantEditing
            ? html`<p class="metadata">
                This account already has access as ${existing.Name}. Saving
                replaces its roles with the ones ticked below.
              </p>`
            : nothing
        }
        <div class="role-options" role="group" aria-labelledby="grant-roles">
          <span id="grant-roles" class="metadata">Roles</span>
          ${roleOptions.map(
            role =>
              html`<vaadin-checkbox
                label=${role}
                ?disabled=${disabled}
                .checked=${this.grantRoles.some(r => sameName(r, role))}
                @checked-changed=${(e: CheckboxCheckedChangedEvent) => {
                  const others = this.grantRoles.filter(
                    r => !sameName(r, role)
                  );
                  this.grantRoles = e.detail.value ? [...others, role] : others;
                }}
              ></vaadin-checkbox>`
          )}
        </div>
        <vaadin-details
          id="grant-advanced"
          summary="Advanced"
          .opened=${this.grantAdvancedOpen}
          @opened-changed=${(e: DetailsOpenedChangedEvent) => {
            this.grantAdvancedOpen = e.detail.value;
          }}
        >
          <vaadin-text-field
            label="Database user name"
            helper-text="The user created inside this database for the login (CREATE USER … FOR LOGIN …). Normally the same as the login, e.g. DOMAIN\\account."
            ?disabled=${disabled || !!existing}
            .value=${this.grantUserName}
            @value-changed=${(e: TextFieldValueChangedEvent) => {
              if (e.detail.value === this.grantUserName) return;
              this.grantUserName = e.detail.value;
              this.grantUserNameEdited = true;
            }}
          ></vaadin-text-field>
        </vaadin-details>
        ${this.grantError ? html`<p role="alert" id="grant-error">${this.grantError}</p>` : nothing}
        <div class="toolbar">
          <vaadin-button
            theme="primary"
            ?disabled=${disabled}
            @click=${() => this.saveGrant(desired)}
            >${existing ? 'Save access' : 'Grant access'}</vaadin-button
          >
          ${
            this.grantEditing || this.grantIdentityId || this.grantLogin
              ? html`<vaadin-button
                  ?disabled=${disabled}
                  @click=${() => this.resetGrant()}
                  >Cancel</vaadin-button
                >`
              : nothing
          }
        </div>
      </fieldset>
      <div class="scroll">
        <table id="current-access">
          <caption>
            Current access
          </caption>
          <thead>
            <tr>
              <th>Database user</th>
              <th>Account</th>
              <th>Roles</th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            ${desired.Principals.map(p => {
              const roles = this.presentRoles(desired, p.Name);
              return html`<tr>
                <td>${p.Name}</td>
                <td>${account(p)}</td>
                <td>
                  ${roles.length ? nothing : html`<span class="metadata">No roles</span>`}
                  ${roles.map(
                    role =>
                      html`<span class="role-chip"
                        >${role}<vaadin-button
                          theme="tertiary small icon"
                          aria-label=${`Revoke ${role} from ${p.Name}`}
                          title=${`Revoke ${role}`}
                          ?disabled=${disabled}
                          @click=${() =>
                            this.save(state => {
                              state.Memberships.find(
                                m => m.Principal === p.Name && m.Role === role
                              )!.Present = false;
                            })}
                          ><vaadin-icon
                            icon="vaadin:close-small"
                          ></vaadin-icon></vaadin-button
                      ></span>`
                  )}
                </td>
                <td>${p.Present ? 'Active' : 'Removed'}</td>
                <td class="row-actions">
                  <vaadin-button
                    theme="small"
                    ?disabled=${disabled}
                    @click=${() => this.editAccess(desired, p)}
                    >Edit</vaadin-button
                  >
                  <vaadin-button
                    theme="small"
                    ?disabled=${disabled}
                    @click=${() =>
                      this.save(state => {
                        const item = state.Principals.find(
                          n => n.Name === p.Name
                        )!;
                        item.Present = !item.Present;
                        if (!item.Present)
                          state.Memberships.filter(
                            m => m.Principal === item.Name
                          ).forEach(m => {
                            m.Present = false;
                          });
                      })}
                    >${p.Present ? 'Remove' : 'Restore'}</vaadin-button
                  >
                </td>
              </tr>`;
            })}
          </tbody>
        </table>
      </div>
      ${
        !desired.Principals.length
          ? html`<p>No managed principals yet. Grant access above.</p>`
          : nothing
      }
      <vaadin-details
        id="custom-roles"
        summary=${`Custom roles (${customRoles.length})`}
      >
        <p class="description">
          Only needed when the built-in db_ roles don't fit. DOrc creates and
          drops these roles; object permissions on them come from your database
          scripts. Once created, a custom role appears in the Roles list above.
        </p>
        <fieldset ?disabled=${disabled}>
          <legend>Create a custom role</legend>
          <vaadin-text-field
            label="Role name"
            ?disabled=${disabled}
            .value=${this.customRoleName}
            @value-changed=${(e: TextFieldValueChangedEvent) => {
              this.customRoleName = e.detail.value;
              this.customRoleError = '';
            }}
          ></vaadin-text-field>
          <vaadin-button
            ?disabled=${disabled}
            @click=${() => this.saveCustomRole(desired)}
            >Create role</vaadin-button
          >
          ${this.customRoleError ? html`<p role="alert">${this.customRoleError}</p>` : nothing}
        </fieldset>
        <ul>
          ${customRoles.map(
            r =>
              html`<li>
                ${r.Name} (${r.Present ? 'active' : 'removed'})
                <vaadin-button
                  theme="small"
                  ?disabled=${disabled}
                  @click=${() =>
                    this.save(state => {
                      const item = state.Roles.find(n => n.Name === r.Name)!;
                      item.Present = !item.Present;
                      if (!item.Present)
                        state.Memberships.filter(
                          m => m.Role === item.Name
                        ).forEach(m => {
                          m.Present = false;
                        });
                    })}
                  >${r.Present ? 'Remove' : 'Restore'}</vaadin-button
                >
              </li>`
          )}
        </ul>
      </vaadin-details>
    `;
    return this.view === 'all'
      ? html`<vaadin-details
          id="desired-state-editor"
          summary="Review or edit saved DOrc configuration"
          >${content}</vaadin-details
        >`
      : html`<section id="desired-state-editor">${content}</section>`;
  }

  private renderAudit() {
    const content = html`
      <vaadin-button
        ?disabled=${this.busy}
        @click=${() =>
          this.request(this.api.databaseAccessAudit(this.target), value => {
            this.audit = value;
          })}
        >Refresh audit</vaadin-button
      >
      <ul>
        ${this.audit.map(
          a =>
            html`<li>
              ${a.CreatedUtc} · ${a.Actor} · ${a.Action}: ${a.Status}
              <pre>${a.Detail}</pre>
            </li>`
        )}
      </ul>
    `;
    return this.view === 'all'
      ? html`<vaadin-details
          summary=${`Recent audit and reconciliation outcomes (${this.audit.length})`}
          >${content}</vaadin-details
        >`
      : html`<section id="database-access-audit">
          ${this.audit.length ? nothing : html`<p>No audit entries yet.</p>`}
          ${content}
        </section>`;
  }
}
