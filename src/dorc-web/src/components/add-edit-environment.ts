import { css, LitElement, PropertyValues } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/text-field';
import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/checkbox';
import { Checkbox } from '@vaadin/checkbox';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/icon';
import '../icons/line awesome-svg.js';
import { Notification } from '@vaadin/notification';
import {
  RefDataEnvironmentsApi
} from '../apis/dorc-api';
import type { EnvironmentApiModel } from '../apis/dorc-api';

@customElement('add-edit-environment')
export class AddEditEnvironment extends LitElement {
  @property({ type: Boolean })
  canSubmit = false;
  @property() ErrorMessage = '';

  @query('#env-secure') private envSecureCheckbox!: Checkbox;

  private envValid = false;
  private isNameValid = false;
  private hasUserChanges = false;
  private allEnvNames: string[] | undefined;
  private readonly maxThinClientFieldLength = 50;
  private readonly maxEnvironmentNameLength = 64;

  @property({ type: Boolean }) private addMode = false;
  @property({ type: Boolean }) private readonly = true;
  @property({ type: Boolean }) private savingMetadata = false;

  private originalEnvName: string | undefined;

  static get styles() {
    return css`
      :host {
        display: inline;
      }
      div#div {
        overflow: auto;
        width: calc(100% - 4px);
        height: calc(100vh - 175px);
      }
      vaadin-text-field {
        display: flex;
        align-items: center;
        justify-content: center;
        min-width: 490px;
        padding: 5px;
      }
      vaadin-combo-box {
        min-width: 490px;
        padding: 5px;
      }
      vaadin-combo-box.vaadin-text-field {
        --lumo-space-m: 0px;
      }
      .tooltip {
        position: relative;
        display: inline-block;
      }
      .tooltip .tooltiptext {
        visibility: hidden;
        width: 300px;
        background-color: black;
        color: #fff;
        text-align: center;
        border-radius: 6px;
        padding: 5px 0;
        position: absolute;
        z-index: 1;
      }
      .tooltip:hover .tooltiptext {
        visibility: visible;
      }
      .small-loader {
        border: 2px solid #f3f3f3;
        border-top: 2px solid #3498db;
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
      }
      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }
      vaadin-button {
        margin-top: auto;
      }
      vaadin-button:disabled,
      vaadin-button[disabled] {
        background-color: var(--dorc-border-color);
      }
    `;
  }

  private _environment!: EnvironmentApiModel;

  @property({ type: Object })
  get environment(): EnvironmentApiModel {
    return this._environment;
  }
  set environment(value: EnvironmentApiModel) {
    const oldVal = this._environment;
    if (value === undefined) return;
    this._environment = JSON.parse(JSON.stringify(value));
    this.hasUserChanges = false;
    this._canSubmit();

    if (this._environment) {
      this.originalEnvName = this._environment.EnvironmentName ?? '';
    }
    console.log(`setting environment ${value?.EnvironmentName}`);
    this.requestUpdate('environment', oldVal);
  }

  connectedCallback() {
    super.connectedCallback?.();
    if (this.addMode) {
      this.environment = this.getEmptyEnv();
    }
  }

  private handleFieldChange<T extends Event>(
    handler: (e: T) => void,
    e: T,
    { validate = true } = {}
  ) {
    handler.call(this, e);
    this.hasUserChanges = true;
    if (validate) this._inputValueChanged();
  }

  render() {
    return html`
      <div id="div" ?hidden="${this.hidden}">

        <vaadin-details
          opened
          summary="Environment Required Settings"
          style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px"
        >
          <vaadin-text-field
            id="env-name"
            label="Name"
            maxlength="${this.maxEnvironmentNameLength}"
            title="Maximum length: ${this.maxEnvironmentNameLength} symbols"
            required
            auto-validate
            .value=${this.environment?.EnvironmentName ?? ''}
            @value-changed=${(e: CustomEvent<{ value: string }>) =>
              this.handleFieldChange(this._envNameValueChanged, e)}
            ?readonly=${this.readonly}
          ></vaadin-text-field>
          <table>
            <tr>
              <td>
                <vaadin-checkbox
                  id="env-secure"
                  style="padding-top:10px; padding-left:20px"
                  .checked=${this.environment?.EnvironmentSecure ?? false}
                  @checked-changed=${(e: CustomEvent<{ value: boolean }>) =>
                    this.handleFieldChange(this.updateSecure, e)}
                  class="tooltip"
                  ?disabled=${this.readonly || (this.environment?.EnvironmentIsProd ?? false)}
                  ><label slot="label"
                    >Is Secure<span class="tooltiptext"
                      >Only use explicitly set environment properties, no
                      defaults</span
                    ></label
                  >
                </vaadin-checkbox>
                <vaadin-checkbox
                  id="env-prod"
                  style="padding-left:20px"
                  .checked=${this.environment?.EnvironmentIsProd ?? false}
                  @checked-changed=${(e: CustomEvent<{ value: boolean }>) =>
                    this.handleFieldChange(this.updateIsProd, e)}
                  class="tooltip"
                  ?disabled=${this.readonly}
                ><label slot="label"
                  >Is Production
                  <span class="tooltiptext"
                    >Is Environment considered Production, uses the production
                    deployment runner and account</span
                  ></label>
                </vaadin-checkbox
              >
             </td>
            </tr>
          </table>
          <vaadin-text-field
            id="env-desc"
            class="block"
            label="Description"
            required
            auto-validate
            .value=${this.environment?.Details?.Description ?? ''}
            @value-changed=${(e: CustomEvent<{ value: string }>) =>
              this.handleFieldChange(this._descriptionValueChanged, e)}
            ?readonly=${this.readonly}
          ></vaadin-text-field>
        </vaadin-details>
        <vaadin-details
          closed
          summary="Environment Optional Settings"
          style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px"
        >
          <vaadin-text-field
            id="opt-backup"
            label="Backup Created From"
            auto-validate
            .value=${this.environment?.Details?.RestoredFromSourceDb ?? ''}
            @value-changed=${(e: CustomEvent<{ value: string }>) =>
              this.handleFieldChange(this._backupValueChanged, e)}
            ?readonly=${this.readonly}
          ></vaadin-text-field>
          <vaadin-text-field
            id="opt-file-share"
            label="File Share"
            auto-validate
            .value=${this.environment?.Details?.FileShare ?? ''}
            @value-changed=${(e: CustomEvent<{ value: string }>) =>
              this.handleFieldChange(this._fileShareValueChanged, e)}
            ?readonly=${this.readonly}
          ></vaadin-text-field>
          <vaadin-text-field
            id="opt-thin-client"
            label="Thin Client Server"
            maxlength="${this.maxThinClientFieldLength}"
            title="Maximum length: ${this.maxThinClientFieldLength} symbols"
            auto-validate
            .value=${this.environment?.Details?.ThinClient ?? ''}
            @value-changed=${(e: CustomEvent<{ value: string }>) =>
              this.handleFieldChange(this._thinClientValueChanged, e)}
            ?readonly=${this.readonly}
          ></vaadin-text-field>
          <vaadin-text-field
            id="opt-notes"
            label="Notes"
            auto-validate
            .value=${this.environment?.Details?.Notes ?? ''}
            @value-changed=${(e: CustomEvent<{ value: string }>) =>
              this.handleFieldChange(this._notesValueChanged, e)}
            ?readonly=${this.readonly}
          ></vaadin-text-field>
        </vaadin-details>
        <div style="padding-left: 4px; margin-right: 30px">
          <vaadin-button
            .disabled=${!this.canSubmit || this.readonly || this.savingMetadata}
            @click=${this.saveMetadata}
            >Save
          </vaadin-button>
          ${this.savingMetadata
            ? html` <div class="small-loader"></div> `
            : html``}
        </div>
        <div style="color: var(--dorc-error-color)">${this.ErrorMessage}</div>
      </div>
    `;
  }

  isEmptyOrSpaces(str: unknown) {
    if (typeof str !== 'string') return true;
    return str.trim().length === 0;
  }

  public clearAllFields() {
    this.environment = this.getEmptyEnv();
    this.hasUserChanges = false;
    this._inputValueChanged();
  }

  clearTextField(id: string) {
    const textField = this.shadowRoot?.getElementById(id) as (HTMLElement & { value: string }) | null;
    if (textField) textField.value = '';
  }

  firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);
    const api = new RefDataEnvironmentsApi();
    api.refDataEnvironmentsGetAllEnvironmentNamesGet().subscribe({
      next: (data: string[]) => {
        this.allEnvNames = data;
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done getting environment names')
    });
  }

  updateSecure(e: CustomEvent<{ value: boolean }>) {
    if (this.environment) {
      this.environment.EnvironmentSecure = e.detail.value;
    }
  }
  updateIsProd(e: CustomEvent<{ value: boolean }>) {
    if (this.environment) {
      const isProd = e.detail.value;
      // Production environments must always be secure
      const updatedEnv = JSON.parse(JSON.stringify(this.environment));
      updatedEnv.EnvironmentIsProd = isProd;
      if (isProd) {
        updatedEnv.EnvironmentSecure = true;
      }
      this.environment = updatedEnv;
      // Explicitly update the checkbox
      this.updateComplete.then(() => {
        if (this.envSecureCheckbox && isProd) {
          this.envSecureCheckbox.checked = true;
        }
      });
    }
  }

  public getEmptyEnv(): EnvironmentApiModel {
    return {
      EnvironmentId: 0,
      EnvironmentName: '',
      EnvironmentIsProd: false,
      EnvironmentSecure: false,
      Details: {
        Description: '',
        EnvironmentOwner: 'NotSet',
        EnvironmentOwnerId: '',
        FileShare: '',
        LastUpdated: '',
        Notes: '',
        RestoredFromSourceDb: '',
        ThinClient: ''
      }
    };
  }

  _envNameValueChanged(e: CustomEvent<{ value: string }>) {
    if (this.environment) {
      const name = e.detail.value ?? '';
      this.environment.EnvironmentName = name.trim();
      this._checkName(this.environment.EnvironmentName);
    }
  }

  _descriptionValueChanged(e: CustomEvent<{ value: string }>) {
    if (!this.environment?.Details) return;
    this.environment.Details.Description = e.detail.value ?? '';
    this.requestUpdate('environment');
  }

  _backupValueChanged(e: CustomEvent<{ value: string }>) {
    if (!this.environment?.Details) return;
    this.environment.Details.RestoredFromSourceDb = e.detail.value ?? '';
    this.requestUpdate('environment');
  }

  _fileShareValueChanged(e: CustomEvent<{ value: string }>) {
    if (!this.environment?.Details) return;
    this.environment.Details.FileShare = e.detail.value ?? '';
    this.requestUpdate('environment');
  }

  _thinClientValueChanged(e: CustomEvent<{ value: string }>) {
    if (!this.environment?.Details) return;
    this.environment.Details.ThinClient = e.detail.value ?? '';
    this.requestUpdate('environment');
  }

  _notesValueChanged(e: CustomEvent<{ value: string }>) {
    if (!this.environment?.Details) return;
    this.environment.Details.Notes = e.detail.value ?? '';
    this.requestUpdate('environment');
  }

  _checkName(data: string) {
    const trimmed = (data ?? '').trim();
    const found = this.allEnvNames?.includes(trimmed) ?? false;
    const isSameAsOriginal =
      trimmed.length > 0 && trimmed === (this.originalEnvName ?? '');
    this.isNameValid = trimmed.length > 0 && (!found || isSameAsOriginal);
    this._canSubmit();
  }

  _inputValueChanged() {
    let result = true;
    if (this.environment !== undefined) {
      if (
        this.environment.Details?.Description !== undefined &&
        this.environment.Details?.Description !== null &&
        this.environment.Details?.Description.length < 1
      ) {
        result = false;
      }
      this.envValid = result;
      this._canSubmit();
    }
  }

  _canSubmit() {
    this.canSubmit = this.envValid && this.isNameValid && this.hasUserChanges;
  }

  saveMetadata() {
    if (this.environment) {
      this.canSubmit = false;
      this.savingMetadata = true;

      if (this.environment?.EnvironmentId === 0) {
        const api = new RefDataEnvironmentsApi();
        api
          .refDataEnvironmentsPost({ environmentApiModel: this.environment })
          .subscribe({
            next: (data: EnvironmentApiModel) => {
              this.hasUserChanges = false;
              this.envAdded();
              Notification.show(`Created Environment ${data.EnvironmentName}`, {
                theme: 'success',
                position: 'bottom-start',
                duration: 5000
              });
              this.savingMetadata = false;
            },
            error: (err: any) => {
              console.error(err);
              this.ErrorMessage = this.extractErrorMessage(err);
              this.savingMetadata = false;
            },
            complete: () => console.log('done adding environment')
          });
      } else {
        const api = new RefDataEnvironmentsApi();
        api
          .refDataEnvironmentsPut({ environmentApiModel: this.environment })
          .subscribe({
            next: (data: EnvironmentApiModel) => {
              if (data !== null) {
                this.hasUserChanges = false;
                this.envUpdated(data);
                Notification.show(
                  `Updated Environment ${data.EnvironmentName}`,
                  {
                    theme: 'success',
                    position: 'bottom-start',
                    duration: 5000
                  }
                );
                this.savingMetadata = false;
              }
            },
            error: (err: any) => {
              console.error(err);
              this.ErrorMessage = this.extractErrorMessage(err);
              this.savingMetadata = false;
            },
            complete: () => console.log('done updating environment')
          });
      }
    }
  }

  envUpdated(data: EnvironmentApiModel) {
    const event = new CustomEvent('environment-details-updated', {
      detail: { environment: data },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  envAdded() {
    const event = new CustomEvent('environment-added', {
      detail: { environment: this.environment },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
    this.Reset();
  }

  Reset() {
    this.environment = this.getEmptyEnv();
    this.hasUserChanges = false;
    this.canSubmit = false;
  }

  private extractErrorMessage(err: any): string {
    if (err?.response?.ExceptionMessage) {
      return err.response.ExceptionMessage;
    }
    if (err?.response?.Message) {
      return err.response.Message;
    }
    if (typeof err?.response === 'string') {
      return err.response;
    }
    if (err?.message) {
      return err.message;
    }
    if (typeof err === 'string') {
      return err;
    }
    return 'An unexpected error occurred. Please try again or contact support.';
  }
}
