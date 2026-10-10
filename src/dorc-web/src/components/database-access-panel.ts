import { css, html, LitElement, nothing, PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { firstValueFrom, Observable } from 'rxjs';
import { AjaxError } from 'rxjs/ajax';
import '@vaadin/button';
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
      padding: var(--lumo-space-m);
      box-sizing: border-box;
    }
    details {
      border-top: 6px solid var(--dorc-link-color);
      padding: var(--lumo-space-m);
      background: var(--dorc-bg-secondary);
    }
    summary {
      font-weight: 400;
      cursor: pointer;
    }
    .toolbar,
    fieldset {
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-m);
      padding: var(--lumo-space-m);
      align-items: end;
    }
    label {
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-xs);
    }
    input,
    select,
    textarea {
      font: inherit;
      padding: var(--lumo-space-s);
      color: var(--dorc-text-primary);
      background: var(--dorc-bg-secondary);
      max-width: 100%;
    }
    fieldset {
      border: 1px solid var(--dorc-border-color);
      margin: var(--lumo-space-m) 0;
    }
    table {
      width: 100%;
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
      <details>
        <summary>Database access (new) — SQL users and roles</summary>
        <p>
          The legacy Users and Permissions controls above remain unchanged.
          Saving or importing changes desired state only.
        </p>
        <div class="toolbar">
          <label
            >Database
            <select
              aria-label="Database"
              .value=${String(this.databaseId)}
              ?disabled=${this.busy}
              @change=${(e: Event) => {
                this.databaseId = Number(
                  (e.currentTarget as HTMLSelectElement).value
                );
                this.reset();
              }}
            >
              <option value="0">Select a database</option>
              ${(this.databases ?? []).map(db => html`<option value=${db.Id ?? 0}>${db.ServerName} / ${db.Name}</option>`)}
            </select>
          </label>
          <vaadin-button
            ?disabled=${this.busy || !this.databaseId}
            @click=${this.load}
            >Load database access</vaadin-button
          >
        </div>
        <p role="status">${this.busy ? 'Working…' : this.message}</p>
        ${this.error ? html`<p role="alert">${this.error}</p>` : nothing}
        ${this.status && !this.status.Enabled ? html`<p>Not enabled for this database. An operator must enable the pilot allowlist.</p>` : nothing}
        ${
          this.desired
            ? html`
                <p>
                  Provider: ${this.desired.Provider} · revision
                  ${this.desired.Revision} · ${this.desired.Principals.length}
                  principals · target execution
                  ${this.status?.ExecutionEnabled ? 'enabled' : 'disabled'}
                </p>
                <p>${this.status?.Limitations.join(' ')}</p>
                <fieldset ?disabled=${disabled}>
                  <legend>
                    Add or update a database principal (existing name updates
                    its login mapping)
                  </legend>
                  <label
                    >Database user name
                    <input
                      @input=${(e: Event) => {
                        this.principalName = (
                          e.currentTarget as HTMLInputElement
                        ).value;
                      }}
                  /></label>
                  <label
                    >Existing SQL server login (native users)
                    <input
                      @input=${(e: Event) => {
                        this.loginName = (
                          e.currentTarget as HTMLInputElement
                        ).value;
                      }}
                  /></label>
                  <label
                    >Search AD users/groups
                    <input
                      @input=${(e: Event) => {
                        this.search = (
                          e.currentTarget as HTMLInputElement
                        ).value;
                      }}
                  /></label>
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
                  <label
                    >Directory identity (optional)
                    <select
                      @change=${(e: Event) => {
                        this.directoryId = (
                          e.currentTarget as HTMLSelectElement
                        ).value;
                      }}
                    >
                      <option value="">Native database user</option>
                      ${this.identities.map(i => html`<option value=${i.Id}>${i.DisplayName} (${i.IsGroup ? 'group' : 'user'})</option>`)}
                    </select>
                  </label>
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
                  <label
                    >Role name
                    <input
                      @input=${(e: Event) => {
                        this.roleName = (
                          e.currentTarget as HTMLInputElement
                        ).value;
                      }}
                  /></label>
                  <label
                    ><span>Create/manage custom role</span
                    ><input
                      type="checkbox"
                      @change=${(e: Event) => {
                        this.managedRole = (
                          e.currentTarget as HTMLInputElement
                        ).checked;
                      }}
                  /></label>
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
                        ${r.Name} (${r.Managed ? 'managed' : 'reference-only'},
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
                  <label
                    >Principal
                    <select
                      @change=${(e: Event) => {
                        this.memberPrincipal = (
                          e.currentTarget as HTMLSelectElement
                        ).value;
                      }}
                    >
                      <option value="">Select principal</option>
                      ${this.desired.Principals.filter(p => p.Present).map(p => html`<option>${p.Name}</option>`)}
                    </select></label
                  >
                  <label
                    >Role
                    <select
                      @change=${(e: Event) => {
                        this.memberRole = (
                          e.currentTarget as HTMLSelectElement
                        ).value;
                      }}
                    >
                      <option value="">Select role</option>
                      ${this.desired.Roles.filter(r => r.Present).map(r => html`<option>${r.Name}</option>`)}
                    </select></label
                  >
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
                        <details>
                          <summary>
                            Observed principals and role memberships
                          </summary>
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
                        </details>
                      `
                    : nothing
                }
                <details>
                  <summary>Import legacy Endur / SQL users</summary>
                  <p>
                    Map each legacy permission name explicitly to an existing
                    database role. Windows records require a legacy-user-ID to
                    stable AD-ID mapping. Import does not apply target changes.
                  </p>
                  <fieldset ?disabled=${disabled}>
                    <label
                      >Permission-to-role mappings (JSON)
                      <textarea
                        rows="4"
                        .value=${this.roleMappings}
                        @input=${(e: Event) => {
                          this.roleMappings = (
                            e.currentTarget as HTMLTextAreaElement
                          ).value;
                          this.imported = undefined;
                        }}
                      ></textarea>
                    </label>
                    <label
                      >Legacy-user-ID to directory-ID mappings (JSON)
                      <textarea
                        rows="4"
                        .value=${this.directoryMappings}
                        @input=${(e: Event) => {
                          this.directoryMappings = (
                            e.currentTarget as HTMLTextAreaElement
                          ).value;
                          this.imported = undefined;
                        }}
                      ></textarea>
                    </label>
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
                </details>
                <details>
                  <summary>
                    Recent audit and reconciliation outcomes
                    (${this.audit.length})
                  </summary>
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
                </details>
              `
            : nothing
        }
      </details>
    `;
  }
}
