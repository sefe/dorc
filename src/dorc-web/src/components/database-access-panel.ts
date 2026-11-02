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
import './hegs-json-viewer';
import type { ComboBoxValueChangedEvent } from '@vaadin/combo-box';
import type { TextFieldValueChangedEvent } from '@vaadin/text-field';
import type { CheckboxCheckedChangedEvent } from '@vaadin/checkbox';
import type { DetailsOpenedChangedEvent } from '@vaadin/details';
import type { RadioGroupValueChangedEvent } from '@vaadin/radio-group';
import {
  DatabaseAccessApi,
  DatabaseAccessAuditApiModel,
  DatabaseAccessDirectoryIdentity,
  DatabaseAccessImportPreview,
  DatabaseAccessImportRequest,
  DatabaseAccessPrincipal,
  DatabaseAccessState,
  DatabaseAccessStatus,
  DatabaseApiModel,
  RefDataDatabaseUsersApi,
  UserDbPermissionApiModel
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

interface LegacyImportAccount {
  id: number;
  login: string;
  kind: string;
  permissions: string[];
  assignmentCount: number;
}

interface ImportDirectoryReview {
  identity?: DatabaseAccessDirectoryIdentity;
  explanation: string;
}

export type DatabaseAccessView = 'all' | 'migrate' | 'permissions' | 'audit';

type GrantKind = 'directory' | 'sql';

/** Fixed database roles that ordinary editing may grant; db_owner needs import approval and the others are protected. */
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
const sameName = (a?: string | null, b?: string | null) =>
  (a ?? '').toLowerCase() === (b ?? '').toLowerCase();
const isDbOwner = (role: string) => sameName(role, 'db_owner');

/** Legacy logins that have neither been imported nor deliberately excluded. */
export function unmigratedLegacyAccounts(
  assignments: UserDbPermissionApiModel[],
  state: DatabaseAccessState
) {
  const handled = new Set<number>(state.ExcludedLegacyUserIds ?? []);
  for (const principal of state.Principals)
    if (principal.LegacyUserId) handled.add(principal.LegacyUserId);
  const logins = assignments
    .filter(a => !a.UserId || !handled.has(a.UserId))
    .map(a => a.UserLoginId?.trim() || `legacy ID ${a.UserId ?? '?'}`);
  return [...new Set(logins)].sort((a, b) => a.localeCompare(b));
}

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
  @state() private imported?: DatabaseAccessImportPreview;
  @state() private audit: DatabaseAccessAuditApiModel[] = [];
  @state() private identities: DatabaseAccessDirectoryIdentity[] = [];
  @state() private directoryNames: Record<string, string> = {};
  @state() private busy = false;
  @state() private error = '';
  @state() private message = '';
  @state() private legacyAccounts: LegacyImportAccount[] = [];
  @state() private legacyAssignmentCount = 0;
  @state() private legacyLoaded = false;
  @state() private importRoles: string[] = [];
  @state() private importRoleOptions: string[] = [];
  @state() private roleMappings: Record<string, string> = {};
  @state() private directoryMappings: Record<string, string> = {};
  @state() private excludedLegacyUserIds: number[] = [];
  @state() private approvedDbOwnerLegacyUserIds: number[] = [];
  @state() private importDirectoryReviews: Record<
    number,
    ImportDirectoryReview
  > = {};
  @state() private importAccountId?: number;
  @state() private importSearch = '';
  @state() private importIdentities: DatabaseAccessDirectoryIdentity[] = [];
  @state() private importIdentityId = '';
  @state() private importLookupMessage = '';
  private epoch = 0;
  private readonly api = new DatabaseAccessApi(dorcApiConfiguration);
  private readonly legacyApi = new RefDataDatabaseUsersApi(
    dorcApiConfiguration
  );
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
  private confirmedMappings?: DatabaseAccessImportRequest;

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
    .import-review td {
      vertical-align: top;
    }
    .import-review .metadata {
      display: block;
      margin-top: var(--lumo-space-xs);
    }
    .import-review vaadin-combo-box {
      margin: 0;
    }
    .import-counts {
      font-weight: 600;
    }
    .import-review td.row-controls {
      white-space: nowrap;
      width: 1%;
    }
    .import-review tr.import-ready td {
      background: var(--dorc-success-bg);
    }
    .import-review tr.import-attention td {
      background: var(--dorc-warning-bg);
    }
    .import-review tr.import-excluded td {
      background: var(--dorc-failure-bg);
    }
    .visually-hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip: rect(0 0 0 0);
      white-space: nowrap;
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
        if (
          this.isConnected &&
          this.selectedDatabaseId === selectedDatabaseId &&
          this.databaseId === selectedDatabaseId
        ) {
          await this.load();
          if (
            this.view === 'migrate' &&
            this.desired &&
            !this.readonly &&
            this.isConnected &&
            this.databaseId === selectedDatabaseId
          )
            await this.prepareImport();
        }
      });
    }
  }

  disconnectedCallback() {
    this.epoch++;
    super.disconnectedCallback();
  }

  private reset() {
    this.epoch++;
    this.status = undefined;
    this.desired = undefined;
    this.imported = undefined;
    this.audit = [];
    this.identities = [];
    this.directoryNames = {};
    this.confirmedMappings = undefined;
    this.resetGrant();
    this.sqlLogins = undefined;
    this.sqlLoginsMessage = '';
    this.customRoleName = '';
    this.customRoleError = '';
    this.resetImport();
    this.busy = false;
    this.error = '';
    this.message = '';
  }

  private get target() {
    return { envId: this.envId, databaseId: this.databaseId };
  }

  private async request<T>(
    source: Observable<T>,
    accept: (value: T) => void,
    clearMessages = true
  ) {
    const epoch = this.epoch;
    this.busy = true;
    if (clearMessages) {
      this.error = '';
      this.message = '';
    }
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
    if (!this.databaseId) return;
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
    for (const principal of this.desired?.Principals ?? []) {
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
    this.invalidateImport();
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
  private grantExisting(): DatabaseAccessPrincipal | undefined {
    const principals = this.desired?.Principals ?? [];
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

  private presentRoles(principal: string) {
    return (this.desired?.Memberships ?? [])
      .filter(m => m.Present && m.Principal === principal)
      .map(m => m.Role);
  }

  private grantAccountChanged() {
    this.grantError = '';
    const existing = this.grantExisting();
    if (existing) {
      this.grantUserName = existing.Name;
      this.grantUserNameEdited = false;
      this.grantRoles = this.presentRoles(existing.Name).filter(
        r => !isDbOwner(r)
      );
      return;
    }
    if (this.grantUserNameEdited) return;
    this.grantUserName =
      this.grantKind === 'directory'
        ? (this.identities.find(i => i.Id === this.grantIdentityId)
            ?.AccountName ?? '')
        : this.grantLogin.trim();
    // Without a DOMAIN\account name the user must choose one explicitly.
    if (
      this.grantKind === 'directory' &&
      this.grantIdentityId &&
      !this.grantUserName
    )
      this.grantAdvancedOpen = true;
  }

  private grantRoleOptions() {
    const names = [...grantableFixedRoles];
    for (const role of this.desired?.Roles ?? [])
      if (
        role.Present &&
        !protectedRoles.some(p => sameName(p, role.Name)) &&
        !names.some(n => sameName(n, role.Name))
      )
        names.push(role.Name);
    return names;
  }

  private searchDirectory() {
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
        this.grantError = value.length
          ? ''
          : `No AD users or groups match '${search}'.`;
        this.grantAccountChanged();
      }
    );
  }

  private async loadSqlLogins() {
    if (this.sqlLogins || !this.databaseId) return;
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

  private async editAccess(principal: DatabaseAccessPrincipal) {
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
    this.grantRoles = this.presentRoles(principal.Name).filter(
      r => !isDbOwner(r)
    );
    await this.updateComplete;
    this.shadowRoot
      ?.querySelector('#grant-access')
      ?.scrollIntoView?.({ block: 'nearest' });
  }

  private async saveGrant() {
    if (!this.desired) return;
    const existing = this.grantExisting();
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
      if (this.desired.Principals.some(p => sameName(p.Name, name)))
        return fail(
          `A database user named '${name}' already exists for a different account. Choose another name under Advanced.`,
          true
        );
    }
    const roles = this.grantRoles;
    const saved = await this.save(state => {
      const principal = state.Principals.find(p => p.Name === name);
      if (principal) principal.Present = true;
      else
        state.Principals.push(
          this.grantKind === 'directory'
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
        if (membership.Principal === name && !isDbOwner(membership.Role))
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

  private saveCustomRole() {
    const name = this.customRoleName.trim();
    const existing = this.desired?.Roles.find(r => sameName(r.Name, name));
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

  private invalidateImport(clearApprovals = true) {
    this.imported = undefined;
    this.confirmedMappings = undefined;
    if (clearApprovals) this.approvedDbOwnerLegacyUserIds = [];
  }

  private resetImport() {
    this.invalidateImport();
    this.legacyAccounts = [];
    this.legacyAssignmentCount = 0;
    this.legacyLoaded = false;
    this.importRoles = [];
    this.importRoleOptions = [];
    this.roleMappings = {};
    this.directoryMappings = {};
    this.excludedLegacyUserIds = [];
    this.importDirectoryReviews = {};
    this.importAccountId = undefined;
    this.importSearch = '';
    this.importIdentities = [];
    this.importIdentityId = '';
    this.importLookupMessage = '';
  }

  private readLegacyAccounts(assignments: UserDbPermissionApiModel[]) {
    const accounts = new Map<number, LegacyImportAccount>();
    for (const assignment of assignments) {
      const {
        UserId: id,
        UserLoginId: login,
        UserLoginType: kind,
        PermissionName: permission
      } = assignment;
      if (
        assignment.DbId !== this.databaseId ||
        !Number.isInteger(id) ||
        !id ||
        id < 1 ||
        !login?.trim() ||
        !kind?.trim() ||
        !permission?.trim()
      )
        throw new Error(
          'Legacy assignments contain missing account/permission data or belong to another database. No mappings were prepared.'
        );
      const existing = accounts.get(id);
      if (
        existing &&
        (existing.login.toLowerCase() !== login.trim().toLowerCase() ||
          existing.kind !== kind.trim().toLowerCase())
      )
        throw new Error(
          `Legacy account ${id} has conflicting identities. Resolve the legacy data before importing.`
        );
      const account = existing ?? {
        id,
        login: login.trim(),
        kind: kind.trim().toLowerCase(),
        permissions: [],
        assignmentCount: 0
      };
      account.assignmentCount++;
      if (!account.permissions.includes(permission))
        account.permissions.push(permission);
      accounts.set(id, account);
    }
    return [...accounts.values()].sort((a, b) =>
      a.login.localeCompare(b.login)
    );
  }

  private async prepareImport() {
    if (this.busy || this.readonly || !this.desired) return;
    const database = this.databases.find(db => db.Id === this.databaseId);
    if (!database?.ServerName || !database.Name) {
      this.error =
        'The selected database needs an exact server and database name.';
      return;
    }
    const target = this.target;
    const epoch = this.epoch;
    const desired = this.desired;
    this.resetImport();
    this.busy = true;
    this.error = '';
    this.message = '';
    try {
      const assignments = await firstValueFrom(
        this.legacyApi.refDataDatabaseUsersGetDbUsersPermissionsGet({
          serverName: database.ServerName,
          databaseName: database.Name
        })
      );
      if (!this.isCurrent(epoch)) return;
      const accounts = this.readLegacyAccounts(assignments);
      this.legacyAccounts = accounts;
      this.legacyAssignmentCount = assignments.length;
      this.importRoles = [
        ...new Set(accounts.flatMap(account => account.permissions))
      ].sort();
      this.importRoleOptions = [
        ...new Set([
          ...this.importRoles,
          ...desired.Roles.map(role => role.Name)
        ])
      ].sort();
      const roles: [string, string][] = [];
      for (const permission of this.importRoles) {
        const matches = desired.Roles.filter(
          role => role.Name.toLowerCase() === permission.toLowerCase()
        );
        roles.push([
          permission,
          matches.length === 1 ? matches[0].Name : permission
        ]);
      }
      this.roleMappings = Object.fromEntries(roles);
      for (const account of accounts.filter(a => a.kind === 'windows')) {
        if (!this.isCurrent(epoch)) return;
        const existing = desired.Principals.find(
          p => p.LegacyUserId === account.id
        );
        try {
          const identity = await firstValueFrom(
            existing?.DirectoryId
              ? this.api.databaseAccessResolve({
                  ...target,
                  id: existing.DirectoryId
                })
              : this.api.databaseAccessLookup({
                  ...target,
                  accountName: account.login
                })
          );
          if (!this.isCurrent(epoch)) return;
          const kind = identity.IsGroup ? 'DirectoryGroup' : 'DirectoryUser';
          if (!identity.Id || identity.Status !== 'Resolved')
            throw new Error(
              'The API did not return a verified directory identity.'
            );
          if (
            existing?.DirectoryId &&
            (identity.Id !== existing.DirectoryId || existing.Kind !== kind)
          )
            throw new Error(
              'The resolved directory identity does not match the saved DOrc identity.'
            );
          this.setImportIdentity(
            account.id,
            identity,
            existing?.DirectoryId
              ? 'Verified from the saved DOrc identity binding.'
              : 'Automatically matched to the exact Windows account and verified in AD.'
          );
        } catch (error) {
          console.error(
            'Legacy directory identity could not be resolved',
            error
          );
          if (!this.isCurrent(epoch)) return;
          this.importDirectoryReviews = {
            ...this.importDirectoryReviews,
            [account.id]: {
              explanation: `${databaseAccessError(error)} Choose an identity manually or explicitly exclude this account.`
            }
          };
        }
      }
      if (!this.isCurrent(epoch)) return;
      const excluded = desired.ExcludedLegacyUserIds ?? [];
      this.excludedLegacyUserIds = accounts
        .filter(
          account =>
            excluded.includes(account.id) &&
            this.canExcludeImportAccount(account)
        )
        .map(account => account.id);
      if (this.excludedLegacyUserIds.length) {
        const mappings = { ...this.directoryMappings };
        for (const id of this.excludedLegacyUserIds)
          delete mappings[String(id)];
        this.directoryMappings = mappings;
      }
      this.legacyLoaded = true;
      if (assignments.length) {
        const mappings = this.importRequest();
        const value = await firstValueFrom(
          this.api.databaseAccessImportPreview({
            ...target,
            databaseAccessImportRequest: mappings
          })
        );
        if (this.isCurrent(epoch)) this.acceptImportPreview(value, mappings);
      }
    } catch (error) {
      console.error('Legacy import preparation failed', error);
      if (this.isCurrent(epoch)) this.error = databaseAccessError(error);
    } finally {
      if (this.isCurrent(epoch)) this.busy = false;
    }
  }

  private setImportIdentity(
    id: number,
    identity: DatabaseAccessDirectoryIdentity,
    explanation: string
  ) {
    this.directoryMappings = { ...this.directoryMappings, [id]: identity.Id };
    this.importDirectoryReviews = {
      ...this.importDirectoryReviews,
      [id]: { identity, explanation }
    };
    this.invalidateImport();
  }

  private async chooseImportIdentity(account: LegacyImportAccount) {
    if (
      this.busy ||
      this.readonly ||
      this.excludedLegacyUserIds.includes(account.id)
    )
      return;
    const epoch = this.epoch;
    this.importAccountId = account.id;
    this.importSearch = account.login.split('\\').pop() ?? account.login;
    this.importIdentities = [];
    this.importIdentityId = '';
    this.importLookupMessage = '';
    await this.updateComplete;
    if (!this.isCurrent(epoch) || this.importAccountId !== account.id) return;
    const picker = this.renderRoot.querySelector<HTMLElement>(
      '#import-directory-picker'
    );
    picker?.scrollIntoView({ block: 'center', inline: 'nearest' });
    picker
      ?.querySelector<HTMLElement>('vaadin-text-field')
      ?.focus({ preventScroll: true });
  }

  private async searchImportDirectory() {
    if (this.busy || this.readonly || this.importAccountId === undefined)
      return;
    const epoch = this.epoch;
    this.importIdentities = [];
    this.importIdentityId = '';
    this.importLookupMessage = '';
    const found = await this.request(
      this.api.databaseAccessDirectory({
        ...this.target,
        search: this.importSearch.trim()
      }),
      identities => {
        this.importIdentities = identities;
        if (!identities.length)
          this.importLookupMessage =
            'No directory identities found. Try another search; no mapping was changed.';
      }
    );
    if (!found && this.isCurrent(epoch)) this.importLookupMessage = this.error;
  }

  private async useImportIdentity() {
    const accountId = this.importAccountId;
    const selected = this.importIdentities.find(
      i => i.Id === this.importIdentityId
    );
    if (this.busy || this.readonly || accountId === undefined || !selected)
      return;
    const epoch = this.epoch;
    const resolved = await this.request(
      this.api.databaseAccessResolve({
        ...this.target,
        id: selected.Id
      }),
      identity => {
        if (
          identity.Id !== selected.Id ||
          identity.IsGroup !== selected.IsGroup
        )
          throw new Error(
            'The directory identity changed. Search again before choosing it.'
          );
        this.setImportIdentity(
          accountId,
          identity,
          'Manually selected and verified. Review that this is the intended account.'
        );
        this.importAccountId = undefined;
      }
    );
    if (!resolved && this.isCurrent(epoch))
      this.importLookupMessage = this.error;
  }

  private importRequest(): DatabaseAccessImportRequest {
    return {
      RoleMappings: { ...this.roleMappings },
      DirectoryMappings: { ...this.directoryMappings },
      ExcludedLegacyUserIds: [...this.excludedLegacyUserIds].sort(
        (a, b) => a - b
      ),
      ApprovedDbOwnerLegacyUserIds: [...this.approvedDbOwnerLegacyUserIds].sort(
        (a, b) => a - b
      ),
      Token: ''
    };
  }

  private canExcludeImportAccount(account: LegacyImportAccount) {
    return (
      account.kind === 'windows' &&
      !this.desired?.Principals.some(
        p =>
          p.LegacyUserId === account.id ||
          p.Name.toLowerCase() === account.login.toLowerCase()
      )
    );
  }

  private importRowStatus(account: LegacyImportAccount) {
    if (this.excludedLegacyUserIds.includes(account.id)) return 'excluded';
    if (
      (account.kind === 'windows' &&
        !Object.hasOwn(this.directoryMappings, String(account.id))) ||
      !['windows', 'sql', 'endur'].includes(account.kind) ||
      (this.mapsToDbOwner(account) &&
        !this.hasApprovedDbOwner(account) &&
        !this.approvedDbOwnerLegacyUserIds.includes(account.id))
    )
      return 'attention';
    return 'ready';
  }

  private mapsToDbOwner(account: LegacyImportAccount) {
    return (
      !this.excludedLegacyUserIds.includes(account.id) &&
      account.permissions.some(
        permission =>
          Object.hasOwn(this.roleMappings, permission) &&
          this.roleMappings[permission].toLowerCase() === 'db_owner'
      )
    );
  }

  private hasApprovedDbOwner(account: LegacyImportAccount) {
    const principal = this.desired?.Principals.find(
      p => p.LegacyUserId === account.id
    );
    return (
      !!principal &&
      !!this.desired?.Memberships.some(
        m =>
          m.Present &&
          m.Role.toLowerCase() === 'db_owner' &&
          m.Principal.toLowerCase() === principal.Name.toLowerCase()
      )
    );
  }

  private canApproveDbOwnerAccount(account: LegacyImportAccount) {
    return (
      !!this.status?.CanApproveDbOwner &&
      this.mapsToDbOwner(account) &&
      !this.hasApprovedDbOwner(account) &&
      (['sql', 'endur'].includes(account.kind) ||
        !!this.importDirectoryReviews[account.id]?.identity)
    );
  }

  private setDbOwnerApproval(account: LegacyImportAccount, approved: boolean) {
    if (
      this.busy ||
      this.readonly ||
      !this.canApproveDbOwnerAccount(account) ||
      this.approvedDbOwnerLegacyUserIds.includes(account.id) === approved
    )
      return;
    this.approvedDbOwnerLegacyUserIds = approved
      ? [...this.approvedDbOwnerLegacyUserIds, account.id]
      : this.approvedDbOwnerLegacyUserIds.filter(id => id !== account.id);
    this.invalidateImport(false);
  }

  private importedDbOwnerNames() {
    return (this.imported?.Proposed.Memberships ?? [])
      .filter(m => m.Present && m.Role.toLowerCase() === 'db_owner')
      .map(m => m.Principal);
  }

  private setImportInclusion(account: LegacyImportAccount, included: boolean) {
    if (
      this.busy ||
      this.readonly ||
      !this.canExcludeImportAccount(account) ||
      this.excludedLegacyUserIds.includes(account.id) !== included
    )
      return;
    const mappings = { ...this.directoryMappings };
    if (included) {
      this.excludedLegacyUserIds = this.excludedLegacyUserIds.filter(
        id => id !== account.id
      );
      const identity = this.importDirectoryReviews[account.id]?.identity;
      if (identity) mappings[String(account.id)] = identity.Id;
    } else {
      this.excludedLegacyUserIds = [...this.excludedLegacyUserIds, account.id];
      delete mappings[String(account.id)];
    }
    this.directoryMappings = mappings;
    if (this.importAccountId === account.id) this.importAccountId = undefined;
    this.invalidateImport();
  }

  private clearImportMapping(account: LegacyImportAccount) {
    const mappings = { ...this.directoryMappings };
    delete mappings[String(account.id)];
    this.directoryMappings = mappings;
    this.importDirectoryReviews = {
      ...this.importDirectoryReviews,
      [account.id]: {
        explanation: 'Mapping cleared. Choose a verified directory identity.'
      }
    };
    this.invalidateImport();
  }

  private acceptImportPreview(
    value: DatabaseAccessImportPreview,
    mappings: DatabaseAccessImportRequest
  ) {
    if (value.SourceAssignments !== this.legacyAssignmentCount)
      throw new Error(
        'Legacy assignments changed. Reload legacy access and review the mappings again.'
      );
    const requestedExclusions = mappings.ExcludedLegacyUserIds ?? [];
    const exclusions = value.ExcludedAccounts ?? [];
    if (
      exclusions.length !== requestedExclusions.length ||
      new Set(exclusions.map(e => e.LegacyUserId)).size !== exclusions.length ||
      exclusions.some(exclusion => {
        const account = this.legacyAccounts.find(
          a => a.id === exclusion.LegacyUserId
        );
        return (
          !requestedExclusions.includes(exclusion.LegacyUserId) ||
          !account ||
          exclusion.LoginName !== account.login ||
          exclusion.AssignmentCount !== account.assignmentCount ||
          exclusion.Permissions.length !== account.permissions.length ||
          !exclusion.Permissions.every(permission =>
            account.permissions.includes(permission)
          ) ||
          value.Proposed.Principals.some(
            p =>
              p.LegacyUserId === account.id ||
              p.Name.toLowerCase() === account.login.toLowerCase()
          )
        );
      })
    )
      throw new Error(
        'The API did not confirm the selected account exclusions. Reload legacy access; the API must support migration exclusions before importing.'
      );
    const requestedApprovals = mappings.ApprovedDbOwnerLegacyUserIds ?? [];
    const approvals = value.ApprovedDbOwnerLegacyUserIds ?? [];
    if (
      !value.Errors.length &&
      (approvals.length !== requestedApprovals.length ||
        new Set(approvals).size !== approvals.length ||
        approvals.some(
          id =>
            !requestedApprovals.includes(id) ||
            !value.Proposed.Principals.some(
              p =>
                p.LegacyUserId === id &&
                value.Proposed.Memberships.some(
                  m =>
                    m.Present &&
                    m.Role.toLowerCase() === 'db_owner' &&
                    m.Principal.toLowerCase() === p.Name.toLowerCase()
                )
            )
        ) ||
        value.Proposed.Memberships.some(
          m =>
            m.Present &&
            m.Role.toLowerCase() === 'db_owner' &&
            !value.Proposed.Principals.some(
              p =>
                p.Name.toLowerCase() === m.Principal.toLowerCase() &&
                (requestedApprovals.includes(p.LegacyUserId ?? -1) ||
                  this.legacyAccounts.some(
                    a => a.id === p.LegacyUserId && this.hasApprovedDbOwner(a)
                  ))
            )
        ))
    )
      throw new Error(
        'The API did not confirm the db_owner approvals. Reload and generate a fresh preview using an API that supports privileged membership approval.'
      );
    this.imported = value;
    this.confirmedMappings = { ...mappings, Token: value.Token };
  }

  private previewImport() {
    if (
      this.busy ||
      this.readonly ||
      !this.legacyLoaded ||
      !this.legacyAssignmentCount
    )
      return;
    const mappings = this.importRequest();
    this.invalidateImport(false);
    void this.request(
      this.api.databaseAccessImportPreview({
        ...this.target,
        databaseAccessImportRequest: mappings
      }),
      value => this.acceptImportPreview(value, mappings)
    );
  }

  private importErrorMessage(error: string) {
    return error.replace(
      /\b(Windows|Legacy) user (\d+)\b/g,
      (message, type: string, id: string) => {
        const account = this.legacyAccounts.find(a => a.id === Number(id));
        return account
          ? `${type} account '${account.login}' (legacy ID ${id})`
          : message;
      }
    );
  }

  private async importLegacy() {
    if (
      !this.confirmedMappings ||
      !this.imported ||
      this.imported.Errors.length ||
      this.importAccountId !== undefined ||
      this.busy ||
      this.readonly
    )
      return;
    const target = this.target;
    const mappings = structuredClone(this.confirmedMappings);
    const epoch = this.epoch;
    const api = this.api;
    this.busy = true;
    const confirmed = await confirmPrompt(
      'Import the preview into the DOrc desired state? Legacy records and target databases are unchanged. Target access has not been checked.' +
        ((this.imported.ExcludedAccounts?.length ?? 0) > 0
          ? ` Exclude these accounts and all their assignments from this import: ${this.imported.ExcludedAccounts!.map(e => `${e.LoginName} (legacy ID ${e.LegacyUserId}, ${e.AssignmentCount} assignments)`).join('; ')}. Their existing SQL access will not be removed or managed.`
          : '') +
        (this.importedDbOwnerNames().length
          ? ` Manage db_owner membership for: ${this.importedDbOwnerNames().join('; ')}. This grants full database control when the deployment applies it to the target.`
          : '')
    );
    if (!this.isCurrent(epoch)) return;
    this.busy = false;
    if (!confirmed) return;
    const imported = await this.request(
      api.databaseAccessExecuteImport({
        ...target,
        databaseAccessImportRequest: mappings
      }),
      () => {
        this.message =
          'Imported desired state. Legacy and target databases were not modified. Target access has not been checked.';
        this.invalidateImport();
      }
    );
    if (imported && this.isCurrent(epoch))
      await this.request(
        api.databaseAccessGet(target),
        value => {
          this.desired = value;
        },
        false
      );
  }

  private isCurrent(epoch: number) {
    return this.isConnected && epoch === this.epoch;
  }

  private renderLegacyImport(disabled: boolean) {
    return this.view === 'all'
      ? html`<vaadin-details
          id="legacy-import"
          summary="Import legacy Endur / SQL users"
          >${this.renderLegacyImportContent(disabled)}</vaadin-details
        >`
      : html`<section id="legacy-import">
          ${this.renderLegacyImportContent(disabled)}
        </section>`;
  }

  private renderLegacyImportContent(disabled: boolean) {
    const selectedAccount = this.legacyAccounts.find(
      a => a.id === this.importAccountId
    );
    const errors = [...new Set(this.imported?.Errors ?? [])];
    const includedPermissions = new Set(
      this.legacyAccounts
        .filter(account => !this.excludedLegacyUserIds.includes(account.id))
        .flatMap(account => account.permissions)
    );
    return html`
      <p class="description">
        Load the existing assignments and review the suggested mappings.
        Same-name roles are suggested from DOrc metadata, not target discovery.
        Saved directory identities are verified; other Windows accounts are
        looked up automatically in AD using their exact DOMAIN\\account name.
        Accounts that cannot be matched still require review. Loading,
        previewing and importing do not connect to the target database. Import
        saves configuration in DOrc only, without validating target logins,
        roles or permissions. If a Windows account cannot be resolved,
        explicitly exclude it from this import. Excluding an account skips all
        its assignments; it does not remove existing SQL access or legacy
        records.
      </p>
      <div class="toolbar">
        <vaadin-button ?disabled=${disabled} @click=${this.prepareImport}
          >${this.legacyLoaded ? 'Reload legacy access' : 'Load legacy access'}</vaadin-button
        >
        ${this.legacyLoaded ? html`<span class="description">Reloading replaces the current mapping selections.</span>` : nothing}
      </div>
      ${
        this.legacyLoaded
          ? html`
              <p class="import-counts">
                ${this.legacyAssignmentCount} legacy assignments across
                ${this.legacyAccounts.length} accounts.
                ${this.excludedLegacyUserIds.length} accounts selected for
                exclusion.
              </p>
              ${
                !this.legacyAssignmentCount
                  ? html`<p>No legacy assignments exist for this database.</p>`
                  : html`
                      <div class="scroll">
                        <table class="import-review" id="import-role-review">
                          <caption>
                            Review permission-to-role mappings. Choose or enter
                            a role name. Its existence is not checked against
                            the target database.
                          </caption>
                          <thead>
                            <tr>
                              <th>Legacy permission</th>
                              <th>Target SQL role</th>
                              <th>Review</th>
                            </tr>
                          </thead>
                          <tbody>
                            ${this.importRoles.map(permission => {
                              if (!includedPermissions.has(permission))
                                return html`<tr>
                                  <td>${permission}</td>
                                  <td colspan="2">
                                    Not imported: all accounts with this
                                    permission are excluded.
                                  </td>
                                </tr>`;
                              const mapped = Object.hasOwn(
                                this.roleMappings,
                                permission
                              )
                                ? this.roleMappings[permission]
                                : '';
                              const roleBlocked = errors.some(
                                error =>
                                  error.includes('protected role') &&
                                  error
                                    .toLowerCase()
                                    .includes(`'${mapped.toLowerCase()}'`)
                              );
                              return html`<tr>
                                <td>${permission}</td>
                                <td>
                                  <vaadin-combo-box
                                    data-permission=${permission}
                                    aria-label=${`Target SQL role for ${permission}`}
                                    placeholder="Choose or enter a role name"
                                    clear-button-visible
                                    allow-custom-value
                                    .items=${this.importRoleOptions}
                                    .value=${mapped}
                                    ?disabled=${disabled}
                                    @value-changed=${(
                                        e: ComboBoxValueChangedEvent
                                      ) => {
                                        if (mapped === e.detail.value) return;
                                        const mappings = {
                                          ...this.roleMappings,
                                          [permission]: e.detail.value
                                        };
                                        if (!e.detail.value)
                                          delete mappings[permission];
                                        this.roleMappings = mappings;
                                        this.invalidateImport();
                                      }}
                                  ></vaadin-combo-box>
                                </td>
                                <td>
                                  ${
                                      roleBlocked
                                        ? html`<span role="alert"
                                            >Role mapping blocked by the API.
                                            Review the validation error below;
                                            do not omit or downgrade access to
                                            bypass it.</span
                                          >`
                                        : mapped.toLowerCase() === 'db_owner'
                                          ? html`<span class="description"
                                              >Full database control. Approve
                                              each intended account below; the
                                              role definition stays
                                              protected.</span
                                            >`
                                          : !mapped
                                            ? html`<span role="alert"
                                                >Choose a role; this permission
                                                will not be silently
                                                omitted.</span
                                              >`
                                            : html`<span class="description"
                                                >Review that this preserves the
                                                intended access.</span
                                              >`
                                    }
                                </td>
                              </tr>`;
                            })}
                          </tbody>
                        </table>
                      </div>
                      <div class="scroll">
                        <table class="import-review" id="import-account-review">
                          <caption>
                            Review existing accounts and their directory
                            identities.
                          </caption>
                          <thead>
                            <tr>
                              <th>
                                <span class="visually-hidden"
                                  >Include in migration</span
                                >
                              </th>
                              <th>Legacy account</th>
                              <th>Permissions</th>
                              <th>Identity mapping</th>
                              <th>Approval</th>
                            </tr>
                          </thead>
                          <tbody>
                            ${this.legacyAccounts.map(account => {
                              const review =
                                this.importDirectoryReviews[account.id];
                              const excluded =
                                this.excludedLegacyUserIds.includes(account.id);
                              return html`<tr
                                data-legacy-user-id=${account.id}
                                class=${`import-${this.importRowStatus(account)}`}
                              >
                                <td class="row-controls">
                                  <vaadin-checkbox
                                    aria-label=${`Include ${account.login} in migration`}
                                    title=${this.canExcludeImportAccount(account) ? `Include ${account.login} in migration` : 'Only Windows accounts that are not yet in DOrc can be left out of the migration'}
                                    .checked=${!excluded}
                                    ?disabled=${disabled || !this.canExcludeImportAccount(account)}
                                    @checked-changed=${(event: CheckboxCheckedChangedEvent) => this.setImportInclusion(account, event.detail.value)}
                                  ></vaadin-checkbox>
                                  ${
                                      account.kind === 'windows'
                                        ? html`<vaadin-button
                                            theme="icon tertiary"
                                            aria-label=${`Choose directory identity for ${account.login}`}
                                            ?disabled=${disabled || excluded}
                                            @click=${() => this.chooseImportIdentity(account)}
                                          >
                                            <vaadin-icon
                                              icon="vaadin:edit"
                                            ></vaadin-icon>
                                            <vaadin-tooltip
                                              slot="tooltip"
                                              text="Choose directory identity"
                                            ></vaadin-tooltip>
                                          </vaadin-button>`
                                        : nothing
                                    }
                                </td>
                                <td>
                                  ${account.login}
                                  <span class="metadata"
                                    >${account.kind} · legacy ID
                                    ${account.id}</span
                                  >
                                </td>
                                <td>${account.permissions.join(', ')}</td>
                                <td>
                                  ${
                                      excluded
                                        ? html`<strong
                                              >Excluded from this import</strong
                                            >
                                            <span class="metadata"
                                              >All ${account.assignmentCount}
                                              assignments will be skipped.
                                              Existing SQL access is
                                              unchanged.</span
                                            >`
                                        : account.kind === 'windows'
                                          ? review?.identity
                                            ? html`${review.identity.DisplayName}
                                                <span class="metadata"
                                                  >Verified
                                                  ${review.identity.IsGroup ? 'group' : 'user'}.
                                                  ${review.explanation}</span
                                                >`
                                            : html`<span role="alert"
                                                >${review?.explanation ?? 'A verified directory identity is required.'}</span
                                              >`
                                          : ['endur', 'sql'].includes(
                                                account.kind
                                              )
                                            ? html`Native SQL login name
                                              preserved: ${account.login}`
                                            : html`<span role="alert"
                                                >Unsupported legacy identity
                                                type: ${account.kind}</span
                                              >`
                                    }
                                </td>
                                <td>
                                  ${
                                      this.mapsToDbOwner(account)
                                        ? html`<div>
                                            ${
                                              this.hasApprovedDbOwner(account)
                                                ? html`<span class="metadata"
                                                    >db_owner membership already
                                                    approved in desired
                                                    state.</span
                                                  >`
                                                : html`<vaadin-checkbox
                                                      data-db-owner-id=${account.id}
                                                      label=${`Approve db_owner for ${account.login}`}
                                                      .checked=${this.approvedDbOwnerLegacyUserIds.includes(account.id)}
                                                      ?disabled=${disabled || !this.canApproveDbOwnerAccount(account)}
                                                      @checked-changed=${(event: CheckboxCheckedChangedEvent) => this.setDbOwnerApproval(account, event.detail.value)}
                                                    ></vaadin-checkbox>
                                                    <span class="metadata"
                                                      >Full database control.
                                                      Requires a verified
                                                      identity and an owner or
                                                      administrator of every
                                                      attached
                                                      environment.</span
                                                    >`
                                            }
                                          </div>`
                                        : nothing
                                    }
                                </td>
                              </tr>`;
                            })}
                          </tbody>
                        </table>
                      </div>
                      ${
                        selectedAccount
                          ? html`
                              <fieldset
                                id="import-directory-picker"
                                ?disabled=${disabled}
                              >
                                <legend>
                                  Choose the directory identity for
                                  ${selectedAccount.login}
                                </legend>
                                <p class="description">
                                  Select the intended account, not just a
                                  similar display name. Choosing a different
                                  identity changes who the imported account
                                  represents.
                                </p>
                                <vaadin-text-field
                                  label="Search directory for this account"
                                  .value=${this.importSearch}
                                  ?disabled=${disabled}
                                  @value-changed=${(
                                      e: TextFieldValueChangedEvent
                                    ) => {
                                      this.importSearch = e.detail.value;
                                    }}
                                ></vaadin-text-field>
                                <vaadin-button
                                  ?disabled=${disabled || this.importSearch.trim().length < 3}
                                  @click=${this.searchImportDirectory}
                                  >Search directory</vaadin-button
                                >
                                <vaadin-combo-box
                                  label="Matching directory identity"
                                  placeholder="Choose a verified account or group"
                                  item-label-path="label"
                                  item-value-path="value"
                                  .items=${this.importIdentities.map(
                                      identity => ({
                                        label: `${identity.DisplayName} (${identity.IsGroup ? 'group' : 'user'}) · ${identity.Id}`,
                                        value: identity.Id
                                      })
                                    )}
                                  .value=${this.importIdentityId}
                                  ?disabled=${disabled}
                                  @value-changed=${(
                                      e: ComboBoxValueChangedEvent
                                    ) => {
                                      this.importIdentityId = e.detail.value;
                                    }}
                                ></vaadin-combo-box>
                                <vaadin-button
                                  ?disabled=${disabled || !this.importIdentityId}
                                  @click=${this.useImportIdentity}
                                  >Use selected identity</vaadin-button
                                >
                                <vaadin-button
                                  ?disabled=${disabled}
                                  @click=${() => {
                                      this.importAccountId = undefined;
                                    }}
                                  >Cancel identity selection</vaadin-button
                                >
                                ${
                                    Object.hasOwn(
                                      this.directoryMappings,
                                      String(selectedAccount.id)
                                    )
                                      ? html`<vaadin-button
                                          aria-label=${`Clear directory mapping for ${selectedAccount.login}`}
                                          ?disabled=${disabled}
                                          @click=${() => this.clearImportMapping(selectedAccount)}
                                          >Clear mapping</vaadin-button
                                        >`
                                      : nothing
                                  }
                                ${this.importLookupMessage ? html`<p role="status">${this.importLookupMessage}</p>` : nothing}
                              </fieldset>
                            `
                          : nothing
                      }
                      <div class="toolbar">
                        <vaadin-button
                          ?disabled=${disabled || this.importAccountId !== undefined}
                          @click=${this.previewImport}
                          >Preview legacy import</vaadin-button
                        >
                        <vaadin-button
                          ?disabled=${disabled || this.importAccountId !== undefined || !this.imported || !!this.imported.Errors.length}
                          @click=${this.importLegacy}
                          >Import confirmed preview</vaadin-button
                        >
                      </div>
                      ${
                        this.imported
                          ? html`
                              <p class="import-counts">
                                ${this.imported.SourceAssignments} legacy
                                assignments;
                                ${this.imported.Proposed.Principals.length}
                                resulting principals;
                                ${this.imported.Proposed.Memberships.length}
                                proposed memberships.
                              </p>
                              ${
                                  this.importedDbOwnerNames().length
                                    ? html`<div
                                        id="import-db-owner-approvals"
                                        role="status"
                                      >
                                        <strong
                                          >Managed db_owner memberships (full
                                          database control):</strong
                                        >
                                        <ul>
                                          ${this.importedDbOwnerNames().map(name => html`<li>${name}</li>`)}
                                        </ul>
                                        <p>
                                          Import records the approved desired
                                          memberships in DOrc only.
                                        </p>
                                      </div>`
                                    : nothing
                                }
                              ${
                                  (this.imported.ExcludedAccounts?.length ??
                                    0) > 0
                                    ? html`<div
                                        id="import-exclusions"
                                        role="status"
                                      >
                                        <p>
                                          <strong
                                            >Explicitly excluded from this
                                            import</strong
                                          >
                                          (existing SQL access and legacy
                                          assignments remain unchanged):
                                        </p>
                                        <ul>
                                          ${this.imported.ExcludedAccounts!.map(
                                            exclusion =>
                                              html`<li>
                                                ${exclusion.LoginName} (legacy
                                                ID ${exclusion.LegacyUserId}):
                                                ${exclusion.AssignmentCount}
                                                assignments;
                                                ${exclusion.Permissions.join(', ')}
                                              </li>`
                                          )}
                                        </ul>
                                      </div>`
                                    : nothing
                                }
                              ${
                                  errors.length
                                    ? html`<p>
                                        Import is blocked. Resolve every issue
                                        before importing; partial proposals must
                                        not be treated as a completed migration.
                                      </p>`
                                    : html`<p>
                                        Metadata preview has no validation
                                        errors. Review the proposed access
                                        before confirming the import. Target
                                        readiness has not been checked; SQL
                                        permissions will not be changed.
                                      </p>`
                                }
                              ${errors.map(error => html`<p role="alert">${this.importErrorMessage(error)}</p>`)}
                              <div class="scroll">
                                <table
                                  class="import-review"
                                  id="import-proposed-access"
                                >
                                  <caption>
                                    Proposed access in DOrc (not yet imported)
                                  </caption>
                                  <thead>
                                    <tr>
                                      <th>Account</th>
                                      <th>Identity</th>
                                      <th>Proposed roles</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    ${this.imported.Proposed.Principals.map(
                                        principal =>
                                          html`<tr>
                                            <td>
                                              ${principal.Name}<span
                                                class="metadata"
                                                >${principal.Present ? 'Present' : 'Marked absent'}</span
                                              >
                                            </td>
                                            <td>
                                              ${principal.Kind}
                                              <span class="metadata"
                                                >${principal.LoginName ?? this.importDirectoryReviews[principal.LegacyUserId ?? -1]?.identity?.DisplayName ?? 'Directory reference (see advanced details)'}</span
                                              >
                                            </td>
                                            <td>
                                              ${this.imported!.Proposed.Memberships.filter(
                                                membership =>
                                                  membership.Principal.toLowerCase() ===
                                                  principal.Name.toLowerCase()
                                              )
                                                .map(
                                                  membership =>
                                                    `${membership.Role}${membership.Present ? '' : ' (revoked)'}`
                                                )
                                                .join(', ')}
                                            </td>
                                          </tr>`
                                      )}
                                  </tbody>
                                </table>
                              </div>
                            `
                          : html`<p>
                              Mappings changed. Generate a new preview before
                              importing.
                            </p>`
                      }
                      <vaadin-details summary="Advanced: generated import JSON">
                        <p class="description">
                          Generated from the review controls above. No
                          hand-written JSON is required.
                        </p>
                        <hegs-json-viewer
                          id="import-mappings-json"
                          .data=${{
                            RoleMappings: this.roleMappings,
                            DirectoryMappings: this.directoryMappings,
                            ExcludedLegacyUserIds: this.excludedLegacyUserIds,
                            ApprovedDbOwnerLegacyUserIds:
                              this.approvedDbOwnerLegacyUserIds
                          }}
                        ></hegs-json-viewer>
                        ${
                          this.imported
                            ? html`<p>Proposed configuration</p>
                                <hegs-json-viewer
                                  id="import-proposal-json"
                                  .data=${this.imported.Proposed}
                                ></hegs-json-viewer>`
                            : nothing
                        }
                      </vaadin-details>
                    `
              }
            `
          : nothing
      }
    `;
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
                migrate:
                  'Migrate legacy Endur / SQL user permissions into DOrc. Untick an account to leave it out of the migration.',
                permissions:
                  'Assign database users, roles and memberships for this database.',
                audit:
                  'Recent changes, imports and reconciliation outcomes for this database.'
              }[this.view]
            }
            Saving or importing records changes the configuration in DOrc only;
            it does not connect to or change the target database.
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
                    configuration. You can continue using the existing Users and
                    Permissions controls.
                  </p>
                </div>`
              : nothing
          }
          ${
            this.desired
              ? html`
                  <p class="metadata">
                    Provider: ${this.desired.Provider} · revision
                    ${this.desired.Revision} · ${this.desired.Principals.length}
                    principals
                  </p>
                  <p class="description">
                    ${this.status?.Limitations.join(' ')}
                  </p>
                  ${
                    this.view === 'all'
                      ? html`<section class="workflow-step">
                            ${this.renderLegacyImport(disabled)}
                            ${this.renderDesiredStateEditor(disabled)}
                          </section>
                          ${this.renderAudit()}`
                      : this.view === 'migrate'
                        ? this.renderLegacyImport(disabled)
                        : this.view === 'permissions'
                          ? this.renderDesiredStateEditor(disabled)
                          : this.renderAudit()
                  }
                `
              : nothing
          }
        </div>
      </div>
    `;
  }

  private renderDesiredStateEditor(disabled: boolean) {
    if (!this.desired) return nothing;
    const desired = this.desired;
    const existing = this.grantExisting();
    const roleOptions = this.grantRoleOptions();
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
                                void this.searchDirectory();
                            }}
                          ></vaadin-text-field>
                          <vaadin-button
                            ?disabled=${disabled}
                            @click=${() => this.searchDirectory()}
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
                      this.grantAccountChanged();
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
                    this.grantAccountChanged();
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
            @click=${() => this.saveGrant()}
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
              const roles = this.presentRoles(p.Name);
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
                    @click=${() => this.editAccess(p)}
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
          ? html`<p>
              No managed principals yet. Grant access above or migrate legacy
              access.
            </p>`
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
            @click=${() => this.saveCustomRole()}
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
