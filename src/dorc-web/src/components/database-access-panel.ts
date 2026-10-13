import { css, html, LitElement, nothing, PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { firstValueFrom, Observable } from 'rxjs';
import { AjaxError } from 'rxjs/ajax';
import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/checkbox';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import './hegs-json-viewer';
import type { ComboBoxValueChangedEvent } from '@vaadin/combo-box';
import type { TextFieldValueChangedEvent } from '@vaadin/text-field';
import type { CheckboxCheckedChangedEvent } from '@vaadin/checkbox';
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

@customElement('database-access-panel')
export class DatabaseAccessPanel extends LitElement {
  @property({ type: Number }) envId = 0;
  @property({ type: Array }) databases: DatabaseApiModel[] = [];
  @property({ type: Boolean }) readonly = true;
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
  private principalName = '';
  private loginName = '';
  private directoryId = '';
  private search = '';
  private roleName = '';
  private managedRole = false;
  @state() private memberPrincipal = '';
  @state() private memberRole = '';
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
      changed.has('envId') ||
      (changed.has('databases') &&
        !this.databases.some(db => db.Id === this.databaseId))
    ) {
      this.databaseId = 0;
      this.reset();
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
    this.principalName = '';
    this.loginName = '';
    this.directoryId = '';
    this.search = '';
    this.roleName = '';
    this.managedRole = false;
    this.memberPrincipal = '';
    this.memberRole = '';
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

  private addPrincipal() {
    const identity = this.identities.find(i => i.Id === this.directoryId);
    const principal: DatabaseAccessPrincipal = {
      Name: this.principalName.trim(),
      Kind: identity
        ? identity.IsGroup
          ? 'DirectoryGroup'
          : 'DirectoryUser'
        : 'Native',
      LoginName: identity ? null : this.loginName.trim(),
      DirectoryId: identity?.Id ?? null,
      Present: true
    };
    this.save(state => {
      const existing = state.Principals.find(
        p => p.Name.toLowerCase() === principal.Name.toLowerCase()
      );
      if (existing) Object.assign(existing, principal);
      else state.Principals.push(principal);
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
      !this.importDirectoryReviews[account.id]?.identity &&
      !this.desired?.Principals.some(
        p =>
          p.LegacyUserId === account.id ||
          p.Name.toLowerCase() === account.login.toLowerCase()
      )
    );
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

  private toggleImportExclusion(account: LegacyImportAccount) {
    if (this.busy || this.readonly || !this.canExcludeImportAccount(account))
      return;
    this.excludedLegacyUserIds = this.excludedLegacyUserIds.includes(account.id)
      ? this.excludedLegacyUserIds.filter(id => id !== account.id)
      : [...this.excludedLegacyUserIds, account.id];
    if (this.importAccountId === account.id) this.importAccountId = undefined;
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
      <vaadin-details
        id="legacy-import"
        summary="Import legacy Endur / SQL users"
      >
        <p class="description">
          Load the existing assignments and review the suggested mappings.
          Same-name roles are suggested from DOrc metadata, not target
          discovery. Saved directory identities are verified; other Windows
          accounts are looked up automatically in AD using their exact
          DOMAIN\\account name. Accounts that cannot be matched still require
          review. Loading, previewing and importing do not connect to the target
          database. Import saves configuration in DOrc only, without validating
          target logins, roles or permissions. If a Windows account cannot be
          resolved, explicitly exclude it from this import. Excluding an account
          skips all its assignments; it does not remove existing SQL access or
          legacy records.
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
                    ? html`<p>
                        No legacy assignments exist for this database.
                      </p>`
                    : html`
                        <div class="scroll">
                          <table class="import-review" id="import-role-review">
                            <caption>
                              Review permission-to-role mappings. Choose or
                              enter a role name. Its existence is not checked
                              against the target database.
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
                          <table
                            class="import-review"
                            id="import-account-review"
                          >
                            <caption>
                              Review existing accounts and their directory
                              identities.
                            </caption>
                            <thead>
                              <tr>
                                <th>Legacy account</th>
                                <th>Permissions</th>
                                <th>Identity mapping</th>
                                <th>Action</th>
                              </tr>
                            </thead>
                            <tbody>
                              ${this.legacyAccounts.map(account => {
                                const review =
                                  this.importDirectoryReviews[account.id];
                                const excluded =
                                  this.excludedLegacyUserIds.includes(
                                    account.id
                                  );
                                return html`<tr
                                  data-legacy-user-id=${account.id}
                                >
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
                                    ${
                                      account.kind === 'windows'
                                        ? html`
                                            <vaadin-button
                                              aria-label=${`Choose directory identity for ${account.login}`}
                                              ?disabled=${disabled || excluded}
                                              @click=${() => this.chooseImportIdentity(account)}
                                              >Choose identity</vaadin-button
                                            >
                                            ${
                                              review?.identity
                                                ? html`<vaadin-button
                                                    aria-label=${`Clear directory mapping for ${account.login}`}
                                                    ?disabled=${disabled}
                                                    @click=${() => {
                                                      const mappings = {
                                                        ...this
                                                          .directoryMappings
                                                      };
                                                      delete mappings[
                                                        String(account.id)
                                                      ];
                                                      this.directoryMappings =
                                                        mappings;
                                                      this.importDirectoryReviews =
                                                        {
                                                          ...this
                                                            .importDirectoryReviews,
                                                          [account.id]: {
                                                            explanation:
                                                              'Mapping cleared. Choose a verified directory identity.'
                                                          }
                                                        };
                                                      this.invalidateImport();
                                                    }}
                                                    >Clear
                                                    mapping</vaadin-button
                                                  >`
                                                : nothing
                                            }
                                            ${
                                              this.canExcludeImportAccount(
                                                account
                                              )
                                                ? html`<vaadin-button
                                                    aria-label=${`${excluded ? 'Include' : 'Exclude'} ${account.login} ${excluded ? 'in' : 'from'} migration`}
                                                    ?disabled=${disabled}
                                                    @click=${() => this.toggleImportExclusion(account)}
                                                    >${excluded ? 'Include in migration' : 'Exclude from migration'}</vaadin-button
                                                  >`
                                                : nothing
                                            }
                                          `
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
                        <vaadin-details
                          summary="Advanced: generated import JSON"
                        >
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
      </vaadin-details>
    `;
  }

  render() {
    const disabled = this.busy || this.readonly;
    return html`
      <div class="panel">
        <div class="content">
          <p class="description">
            Manage SQL database users and roles for this environment. Saving or
            importing records changes the configuration in DOrc only; it does
            not connect to or change the target database.
          </p>
          <div class="toolbar">
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
            <vaadin-button
              ?disabled=${this.busy || !this.databaseId}
              @click=${this.load}
              >Load database access</vaadin-button
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
                  <section class="workflow-step">
                    ${this.renderLegacyImport(disabled)}
                    <vaadin-details
                      id="desired-state-editor"
                      summary="Review or edit saved DOrc configuration"
                    >
                      <p class="description">
                        These controls update desired state in DOrc only, not
                        SQL permissions.
                      </p>
                      <fieldset ?disabled=${disabled}>
                        <legend>
                          Add or update a database principal (existing name
                          updates its login mapping)
                        </legend>
                        <vaadin-text-field
                          label="Database user name"
                          ?disabled=${disabled}
                          .value=${this.principalName}
                          @value-changed=${(e: TextFieldValueChangedEvent) => {
                            this.principalName = e.detail.value;
                          }}
                        ></vaadin-text-field>
                        <vaadin-text-field
                          label="Existing SQL server login (native users)"
                          ?disabled=${disabled}
                          .value=${this.loginName}
                          @value-changed=${(e: TextFieldValueChangedEvent) => {
                            this.loginName = e.detail.value;
                          }}
                        ></vaadin-text-field>
                        <vaadin-text-field
                          label="Search AD users/groups"
                          ?disabled=${disabled}
                          .value=${this.search}
                          @value-changed=${(e: TextFieldValueChangedEvent) => {
                            this.search = e.detail.value;
                          }}
                        ></vaadin-text-field>
                        <vaadin-button
                          ?disabled=${disabled}
                          @click=${() =>
                            this.request(
                              this.api.databaseAccessDirectory({
                                ...this.target,
                                search: this.search
                              }),
                              value => {
                                this.identities = value;
                                this.directoryId = '';
                                if (!value.length)
                                  this.message =
                                    'No directory identities found.';
                              }
                            )}
                          >Search AD</vaadin-button
                        >
                        <vaadin-combo-box
                          label="Directory identity (optional)"
                          placeholder="Native database user"
                          clear-button-visible
                          ?disabled=${disabled}
                          item-label-path="label"
                          item-value-path="value"
                          .items=${this.identities.map(i => ({
                            label: `${i.DisplayName} (${i.IsGroup ? 'group' : 'user'})`,
                            value: i.Id
                          }))}
                          .value=${this.directoryId}
                          @value-changed=${(e: ComboBoxValueChangedEvent) => {
                            this.directoryId = e.detail.value;
                          }}
                        ></vaadin-combo-box>
                        <vaadin-button
                          theme="primary"
                          ?disabled=${disabled}
                          @click=${this.addPrincipal}
                          >Save principal</vaadin-button
                        >
                      </fieldset>
                      <div class="scroll">
                        <table>
                          <caption>
                            Desired database principals
                          </caption>
                          <thead>
                            <tr>
                              <th>Name</th>
                              <th>Type</th>
                              <th>Login / live directory identity</th>
                              <th>Desired</th>
                              <th>Action</th>
                            </tr>
                          </thead>
                          <tbody>
                            ${this.desired.Principals.map(
                              p =>
                                html`<tr>
                                  <td>${p.Name}</td>
                                  <td>${p.Kind}</td>
                                  <td>
                                    ${p.DirectoryId ? (this.directoryNames[p.DirectoryId] ?? p.DirectoryId) : p.LoginName}
                                  </td>
                                  <td>${p.Present ? 'Present' : 'Absent'}</td>
                                  <td>
                                    <vaadin-button
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
                                      >${p.Present ? 'Mark absent' : 'Restore'}</vaadin-button
                                    >
                                  </td>
                                </tr>`
                            )}
                          </tbody>
                        </table>
                      </div>
                      ${!this.desired.Principals.length ? html`<p>No managed principals yet. Add one or preview a legacy import.</p>` : nothing}
                      <fieldset ?disabled=${disabled}>
                        <legend>Add a database role</legend>
                        <vaadin-text-field
                          label="Role name"
                          ?disabled=${disabled}
                          .value=${this.roleName}
                          @value-changed=${(e: TextFieldValueChangedEvent) => {
                            this.roleName = e.detail.value;
                          }}
                        ></vaadin-text-field>
                        <vaadin-checkbox
                          label="Create/manage custom role"
                          ?disabled=${disabled}
                          .checked=${this.managedRole}
                          @checked-changed=${(
                            e: CheckboxCheckedChangedEvent
                          ) => {
                            this.managedRole = e.detail.value;
                          }}
                        ></vaadin-checkbox>
                        <vaadin-button
                          ?disabled=${disabled}
                          @click=${() =>
                            this.save(state => {
                              state.Roles.push({
                                Name: this.roleName.trim(),
                                Managed: this.managedRole,
                                Present: true
                              });
                            })}
                          >Save role</vaadin-button
                        >
                      </fieldset>
                      <ul>
                        ${this.desired.Roles.map(
                          r =>
                            html`<li>
                              ${r.Name}
                              (${r.Managed ? 'managed' : 'reference-only'},
                              ${r.Present ? 'present' : 'absent'})
                              ${
                                r.Managed
                                  ? html`<vaadin-button
                                      ?disabled=${disabled}
                                      @click=${() =>
                                        this.save(state => {
                                          const item = state.Roles.find(
                                            n => n.Name === r.Name
                                          )!;
                                          item.Present = !item.Present;
                                          if (!item.Present)
                                            state.Memberships.filter(
                                              m => m.Role === item.Name
                                            ).forEach(m => {
                                              m.Present = false;
                                            });
                                        })}
                                      >${r.Present ? 'Mark absent' : 'Restore'}</vaadin-button
                                    >`
                                  : nothing
                              }
                            </li>`
                        )}
                      </ul>
                      <fieldset ?disabled=${disabled}>
                        <legend>Assign a role</legend>
                        <vaadin-combo-box
                          label="Principal"
                          placeholder="Select principal"
                          ?disabled=${disabled}
                          .items=${this.desired.Principals.filter(p => p.Present).map(p => p.Name)}
                          .value=${this.memberPrincipal}
                          @value-changed=${(e: ComboBoxValueChangedEvent) => {
                            this.memberPrincipal = e.detail.value;
                          }}
                        ></vaadin-combo-box>
                        <vaadin-combo-box
                          label="Role"
                          placeholder="Select role"
                          ?disabled=${disabled}
                          .items=${this.desired.Roles.filter(r => r.Present).map(r => r.Name)}
                          .value=${this.memberRole}
                          @value-changed=${(e: ComboBoxValueChangedEvent) => {
                            this.memberRole = e.detail.value;
                          }}
                        ></vaadin-combo-box>
                        <vaadin-button
                          theme="primary"
                          ?disabled=${disabled || !this.memberPrincipal || !this.memberRole}
                          @click=${async () => {
                            const saved = await this.save(state => {
                              const existing = state.Memberships.find(
                                m =>
                                  m.Principal === this.memberPrincipal &&
                                  m.Role === this.memberRole
                              );
                              if (existing) existing.Present = true;
                              else
                                state.Memberships.push({
                                  Principal: this.memberPrincipal,
                                  Role: this.memberRole,
                                  Present: true
                                });
                            });
                            if (saved) {
                              this.memberPrincipal = '';
                              this.memberRole = '';
                            }
                          }}
                          >Save assignment</vaadin-button
                        >
                      </fieldset>
                      <ul>
                        ${this.desired.Memberships.map(
                          m =>
                            html`<li>
                              ${m.Principal} → ${m.Role}:
                              ${m.Present ? 'assigned' : 'revoked'}
                              <vaadin-button
                                ?disabled=${disabled || !m.Present}
                                @click=${() =>
                                  this.save(state => {
                                    state.Memberships.find(
                                      n =>
                                        n.Principal === m.Principal &&
                                        n.Role === m.Role
                                    )!.Present = false;
                                  })}
                                >Revoke desired membership</vaadin-button
                              >
                            </li>`
                        )}
                      </ul>
                    </vaadin-details>
                  </section>
                  <vaadin-details
                    summary=${`Recent audit and reconciliation outcomes (${this.audit.length})`}
                  >
                    <vaadin-button
                      ?disabled=${this.busy}
                      @click=${() =>
                        this.request(
                          this.api.databaseAccessAudit(this.target),
                          value => {
                            this.audit = value;
                          }
                        )}
                      >Refresh audit</vaadin-button
                    >
                    <ul>
                      ${this.audit.map(
                        a =>
                          html`<li>
                            ${a.CreatedUtc} · ${a.Actor} · ${a.Action}:
                            ${a.Status}
                            <pre>${a.Detail}</pre>
                          </li>`
                      )}
                    </ul>
                  </vaadin-details>
                `
              : nothing
          }
        </div>
      </div>
    `;
  }
}
