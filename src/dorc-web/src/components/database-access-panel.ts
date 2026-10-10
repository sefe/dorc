import { css, html, LitElement, nothing, PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { firstValueFrom, Observable } from 'rxjs';
import { AjaxError } from 'rxjs/ajax';
import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/checkbox';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import '@vaadin/text-area';
import type { ComboBoxValueChangedEvent } from '@vaadin/combo-box';
import type { TextFieldValueChangedEvent } from '@vaadin/text-field';
import type { TextAreaValueChangedEvent } from '@vaadin/text-area';
import type { CheckboxCheckedChangedEvent } from '@vaadin/checkbox';
import {
  DatabaseAccessApi,
  DatabaseAccessAuditApiModel,
  DatabaseAccessDirectoryIdentity,
  DatabaseAccessImportPreview,
  DatabaseAccessImportRequest,
  DatabaseAccessPreview,
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

export function parseAccessMappings(text: string): Record<string, string> {
  const value: unknown = JSON.parse(text);
  if (!value || typeof value !== 'object' || Array.isArray(value))
    throw new Error(
      'Mappings must be a JSON object of source keys to target strings.'
    );
  const entries = Object.entries(value);
  if (
    entries.some(
      ([key, target]) => !key || typeof target !== 'string' || !target.trim()
    )
  )
    throw new Error(
      'Every mapping requires a nonempty source key and target string.'
    );
  return Object.fromEntries(entries);
}

@customElement('database-access-panel')
export class DatabaseAccessPanel extends LitElement {
  @property({ type: Number }) envId = 0;
  @property({ type: Array }) databases: DatabaseApiModel[] = [];
  @property({ type: Boolean }) readonly = true;
  @state() private databaseId = 0;
  @state() private status?: DatabaseAccessStatus;
  @state() private desired?: DatabaseAccessState;
  @state() private preview?: DatabaseAccessPreview;
  @state() private imported?: DatabaseAccessImportPreview;
  @state() private audit: DatabaseAccessAuditApiModel[] = [];
  @state() private identities: DatabaseAccessDirectoryIdentity[] = [];
  @state() private directoryNames: Record<string, string> = {};
  @state() private busy = false;
  @state() private error = '';
  @state() private message = '';
  private epoch = 0;
  private readonly api = new DatabaseAccessApi(dorcApiConfiguration);
  private principalName = '';
  private loginName = '';
  private directoryId = '';
  private search = '';
  private roleName = '';
  private managedRole = false;
  private memberPrincipal = '';
  private memberRole = '';
  private roleMappings = '{}';
  private directoryMappings = '{}';
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
    this.preview = undefined;
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
    this.roleMappings = '{}';
    this.directoryMappings = '{}';
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
    if (!this.desired || this.busy || this.readonly) return;
    const state = structuredClone(this.desired);
    change(state);
    this.preview = undefined;
    this.imported = undefined;
    void this.request(
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

  private async apply() {
    if (
      !this.preview ||
      this.busy ||
      this.readonly ||
      !this.status?.ExecutionEnabled ||
      this.preview.Errors.length
    )
      return;
    const target = this.target;
    const token = this.preview.Token;
    const epoch = this.epoch;
    const api = this.api;
    this.busy = true;
    const confirmed = await confirmPrompt(
      'Apply the displayed changes to this target database?'
    );
    if (!this.isCurrent(epoch)) return;
    this.busy = false;
    if (!confirmed) return;
    this.preview = undefined;
    const applied = await this.request(
      api.databaseAccessApply({
        ...target,
        databaseAccessConfirmation: { Token: token }
      }),
      value => {
        this.preview = value;
        this.message = 'Target database reconciliation completed.';
      }
    );
    if (applied && this.isCurrent(epoch))
      await this.request(
        api.databaseAccessAudit(target),
        value => {
          this.audit = value;
        },
        false
      );
  }

  private previewImport() {
    try {
      const mappings: DatabaseAccessImportRequest = {
        RoleMappings: parseAccessMappings(this.roleMappings),
        DirectoryMappings: parseAccessMappings(this.directoryMappings),
        Token: ''
      };
      this.imported = undefined;
      void this.request(
        this.api.databaseAccessImportPreview({
          ...this.target,
          databaseAccessImportRequest: mappings
        }),
        value => {
          this.imported = value;
          this.confirmedMappings = { ...mappings, Token: value.Token };
        }
      );
    } catch (error) {
      this.error = databaseAccessError(error);
    }
  }

  private async importLegacy() {
    if (
      !this.confirmedMappings ||
      !this.imported ||
      this.imported.Errors.length ||
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
      'Import the preview into the new desired-state stack? Legacy records and target databases are unchanged.'
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
          'Imported desired state. Legacy and target databases were not modified.';
        this.preview = undefined;
        this.imported = undefined;
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

  render() {
    const disabled = this.busy || this.readonly;
    return html`
      <vaadin-details class="panel" summary="Database Access">
        <div class="content">
          <p class="description">
            Manage SQL database users and roles. The existing Users and
            Permissions controls above remain unchanged. Saving or importing
            records changes the configuration in DOrc only; applying a confirmed
            preview updates the target database.
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
                  <p>
                    Database access management is not enabled for this database.
                  </p>
                  <p class="description">
                    A DOrc administrator must enable
                    <code>DatabaseAccess:Enabled</code> and include database ID
                    <strong>${this.databaseId}</strong> in
                    <code>DatabaseAccess:DatabaseIds</code> in the API
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
                    principals · target execution
                    ${this.status?.ExecutionEnabled ? 'enabled' : 'disabled'}
                  </p>
                  <p class="description">
                    ${this.status?.Limitations.join(' ')}
                  </p>
                  <fieldset ?disabled=${disabled}>
                    <legend>
                      Add or update a database principal (existing name updates
                      its login mapping)
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
                              this.message = 'No directory identities found.';
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
                      @checked-changed=${(e: CheckboxCheckedChangedEvent) => {
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
                      ?disabled=${disabled}
                      @click=${() =>
                        this.save(state => {
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
                        })}
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
                  <div class="toolbar">
                    <vaadin-button
                      ?disabled=${disabled}
                      @click=${() => {
                        this.preview = undefined;
                        void this.request(
                          this.api.databaseAccessPreview(this.target),
                          value => {
                            this.preview = value;
                          }
                        );
                      }}
                      >Discover and preview target changes</vaadin-button
                    >
                    <vaadin-button
                      theme="primary"
                      ?disabled=${disabled || !this.status?.ExecutionEnabled || !this.preview || !!this.preview.Errors.length || !this.preview.Operations.length}
                      @click=${this.apply}
                      >Apply confirmed preview</vaadin-button
                    >
                  </div>
                  ${
                    this.preview
                      ? html`
                          <h3>
                            Target preview: ${this.preview.Operations.length}
                            changes
                          </h3>
                          ${this.preview.Errors.map(e => html`<p role="alert">${e}</p>`)}
                          <ul>
                            ${this.preview.Operations.map(o => html`<li>${o.Action}: ${o.Principal} ${o.Role} ${o.Login}</li>`)}
                          </ul>
                          ${!this.preview.Operations.length && !this.preview.Errors.length ? html`<p>Managed state matches the target. Unmanaged objects are unchanged.</p>` : nothing}
                          <vaadin-details
                            summary="Observed principals and role memberships"
                          >
                            <pre>
${JSON.stringify(
  {
    Principals: this.preview.Observed.Principals,
    Roles: this.preview.Observed.Roles,
    Memberships: this.preview.Observed.Memberships
  },
  null,
  2
)}</pre>
                          </vaadin-details>
                        `
                      : nothing
                  }
                  <vaadin-details summary="Import legacy Endur / SQL users">
                    <p class="description">
                      Map each legacy permission name explicitly to an existing
                      database role. Windows records require a legacy-user-ID to
                      stable AD-ID mapping. Import does not apply target
                      changes.
                    </p>
                    <fieldset ?disabled=${disabled}>
                      <vaadin-text-area
                        label="Permission-to-role mappings (JSON)"
                        ?disabled=${disabled}
                        .value=${this.roleMappings}
                        @value-changed=${(e: TextAreaValueChangedEvent) => {
                          if (this.roleMappings === e.detail.value) return;
                          this.roleMappings = e.detail.value;
                          this.imported = undefined;
                        }}
                      ></vaadin-text-area>
                      <vaadin-text-area
                        label="Legacy-user-ID to directory-ID mappings (JSON)"
                        ?disabled=${disabled}
                        .value=${this.directoryMappings}
                        @value-changed=${(e: TextAreaValueChangedEvent) => {
                          if (this.directoryMappings === e.detail.value) return;
                          this.directoryMappings = e.detail.value;
                          this.imported = undefined;
                        }}
                      ></vaadin-text-area>
                      <vaadin-button
                        ?disabled=${disabled}
                        @click=${this.previewImport}
                        >Preview legacy import</vaadin-button
                      >
                      <vaadin-button
                        ?disabled=${disabled || !this.imported || !!this.imported.Errors.length}
                        @click=${this.importLegacy}
                        >Import confirmed preview</vaadin-button
                      >
                    </fieldset>
                    ${
                      this.imported
                        ? html`<p>
                              ${this.imported.SourceAssignments} legacy
                              assignments;
                              ${this.imported.Proposed.Principals.length}
                              resulting principals.
                            </p>
                            ${this.imported.Errors.map(e => html`<p role="alert">${e}</p>`)}
                            <pre>
${JSON.stringify(this.imported.Proposed, null, 2)}</pre>`
                        : nothing
                    }
                  </vaadin-details>
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
      </vaadin-details>
    `;
  }
}
