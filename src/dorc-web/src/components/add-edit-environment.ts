import { css, LitElement, PropertyValues, render } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/text-field';
import '@vaadin/combo-box';
import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/checkbox';
import { Checkbox } from '@vaadin/checkbox/src/vaadin-checkbox';
import { customElement, property, state } from 'lit/decorators.js';
import './dorc-icon.js';
import { html } from 'lit/html.js';
import { ComboBox, ComboBoxItemModel } from '@vaadin/combo-box';
import '@vaadin/horizontal-layout';
import { TextField } from '@vaadin/text-field';
import { Notification } from '@vaadin/notification';
import { ifDefined } from 'lit/directives/if-defined.js';
import {
  UserElementApiModel,
  RefDataEnvironmentsApi,
  RefDataEnvironmentsUsersApi
} from '../apis/dorc-api';
import type { EnvironmentApiModel } from '../apis/dorc-api';

@customElement('add-edit-environment')
export class AddEditEnvironment extends LitElement {
  @property({ type: Boolean })
  canSubmit = false;

  @property() ErrorMessage = '';

  @property({ type: Array }) searchResults!: UserElementApiModel[];

  @property({ type: Boolean }) searchingUsers = false;

  @property({ type: String }) selectedUser!: string;

  @state() EnvOwnerDisplayName: string | undefined = '';

  private envValid = false;

  private isNameValid = false;

  private allEnvNames: string[] | undefined;

  @property({ type: Boolean }) private addMode = false;

  @property({ type: Boolean }) private readonly = true;

  @property({ type: Boolean }) private savingMetadata = false;

  private searchADValue = '';

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

        /* Position the tooltip */
        position: absolute;
        z-index: 1;
      }

      .tooltip:hover .tooltiptext {
        visibility: visible;
      }

      .small-loader {
        border: 2px solid #f3f3f3; /* Light grey */
        border-top: 2px solid #3498db; /* Blue */
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
        background-color: #dde2e8;
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

    if (
      this._environment &&
      this._environment?.EnvironmentName !== oldVal?.EnvironmentName
    ) {
      this.EnvOwnerDisplayName = undefined;
      this.findDisplayNameForOwner();
      this.checkEnvironmentNameValid(this.environment?.EnvironmentName);
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

  render() {
    return html`
      <div id="div" ?hidden="${this.hidden}">
        <vaadin-details
          opened=${ifDefined(
            this.isEmptyOrSpaces(this.EnvOwnerDisplayName) ? true : undefined
          )}
          style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px;"
        >
          <vaadin-details-summary slot="summary">
            <vaadin-horizontal-layout>
              <div style="padding-right: 5px">Environment Owner:</div>
              <dorc-icon icon="admin"></dorc-icon>
              ${this.isEmptyOrSpaces(this.EnvOwnerDisplayName)
                ? html`<div style="font-style: italic; color: red">
                    Press 'Set Owner' to fill
                  </div> `
                : html` <div style="font-weight: bold;">
                    ${this.EnvOwnerDisplayName}
                  </div>`}
            </vaadin-horizontal-layout>
          </vaadin-details-summary>
          <vaadin-horizontal-layout>
            <vaadin-text-field
              id="search-criteria"
              label="Search Criteria"
              @input="${this.updateSearchCriteria}"
            ></vaadin-text-field>
            <vaadin-button @click="${this.searchAD}" style="margin-bottom: 5px"
              >Search
            </vaadin-button>
            ${this.searchingUsers
              ? html` <div class="small-loader"></div> `
              : html``}
          </vaadin-horizontal-layout>
          <vaadin-horizontal-layout>
            <vaadin-combo-box
              id="searchResults"
              label="Search Results"
              item-value-path="DisplayName"
              item-label-path="DisplayName"
              .items="${this.searchResults}"
              .renderer="${this.searchResultsRenderer}"
              @value-changed="${this.searchResultsValueChanged}"
            ></vaadin-combo-box>
            <vaadin-button
              @click="${this.setNewOwner}"
              style="margin-bottom: 5px; margin-right: 5px"
              >Set Owner
            </vaadin-button>
          </vaadin-horizontal-layout>
        </vaadin-details>
        <vaadin-details
          opened
          summary="Environment Required Settings"
          style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px"
        >
          <vaadin-text-field
            id="env-name"
            label="Name"
            required
            auto-validate
            value="${this.environment?.EnvironmentName ?? ''}"
            @value-changed="${this._envNameValueChanged}"
            ?readonly="${this.readonly}"
          ></vaadin-text-field>
          <table>
            <tr>
              <td>
                <vaadin-checkbox
                  id="env-secure"
                  style="padding-top: 10px; padding-left: 20px"
                  ?checked="${this.environment?.EnvironmentSecure}"
                  @change="${this.updateSecure}"
                  class="tooltip"
                  ?disabled="${this.readonly}"
                  ><label slot="label"
                    >Is Secure<span class="tooltiptext"
                      >Only use explicitly set environment properties, no
                      defaults</span
                    ></label
                  >
                </vaadin-checkbox>
                <vaadin-checkbox
                  id="env-prod"
                  style="padding-left: 20px"
                  ?checked="${this.environment?.EnvironmentIsProd}"
                  @change="${this.updateIsProd}"
                  class="tooltip"
                  ?disabled="${this.readonly}"
                  ><label slot="label"
                    >Is Production<span class="tooltiptext"
                      >Is Environment considered Production, uses the production
                      deployment runner and account</span
                    ></label
                  ></vaadin-checkbox
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
            value="${this.environment?.Details?.Description ?? ''}"
            @value-changed="${this._descriptionValueChanged}"
            ?readonly="${this.readonly}"
          ></vaadin-text-field>
        </vaadin-details>
        <vaadin-details
          closed
          summary="Environment Optional Settings"
          style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px"
        >
          <vaadin-text-field
            id="opt-backup"
            label="Backup Created From"
            auto-validate
            value="${this.environment?.Details?.RestoredFromSourceDb ?? ''}"
            @value-changed="${this._backupValueChanged}"
            ?readonly="${this.readonly}"
          ></vaadin-text-field>
          <vaadin-text-field
            id="opt-file-share"
            label="File Share"
            auto-validate
            value="${this.environment?.Details?.FileShare ?? ''}"
            @value-changed="${this._fileShareValueChanged}"
            ?readonly="${this.readonly}"
          ></vaadin-text-field>
          <vaadin-text-field
            id="opt-thin-client"
            label="Thin Client Server"
            auto-validate
            value="${this.environment?.Details?.ThinClient ?? ''}"
            @value-changed="${this._thinClientValueChanged}"
            ?readonly="${this.readonly}"
          ></vaadin-text-field>
          <vaadin-text-field
            id="opt-notes"
            label="Notes"
            auto-validate
            value="${this.environment?.Details?.Notes ?? ''}"
            @value-changed="${this._notesValueChanged}"
            ?readonly="${this.readonly}"
          ></vaadin-text-field>
        </vaadin-details>

        <div style="padding-left: 4px; margin-right: 30px">
          <vaadin-button
            .disabled="${!this.canSubmit || this.readonly}"
            @click="${this.saveMetadata}"
            >Save
          </vaadin-button>
          ${this.savingMetadata
            ? html` <div class="small-loader"></div> `
            : html``}
        </div>
        <div style="color: #FF3131">${this.ErrorMessage}</div>
      </div>
    `;
  }

  isEmptyOrSpaces(str: any) {
    return str === undefined || str.match(/^ *$/) !== null;
  }

  public clearAllFields() {
    this.EnvOwnerDisplayName = '';
    this.clearTextField('search-criteria');
    const searchResult = this.shadowRoot?.getElementById(
      'searchResults'
    ) as ComboBox;
    if (searchResult) searchResult.selectedItem = undefined;

    const secure = this.shadowRoot?.getElementById('env-secure') as Checkbox;
    if (secure) secure.checked = false;
    const prod = this.shadowRoot?.getElementById('env-prod') as Checkbox;
    if (prod) prod.checked = false;

    this.clearTextField('env-name');
    this.clearTextField('env-desc');

    this.clearTextField('opt-backup');
    this.clearTextField('opt-file-share');
    this.clearTextField('opt-thin-client');
    this.clearTextField('opt-notes');
  }

  clearTextField(id: string) {
    const textField = this.shadowRoot?.getElementById(id) as TextField;
    if (textField) textField.value = '';
  }

  setNewOwner() {
    const found = this.searchResults.find(
      u => u.DisplayName === this.selectedUser
    );
    if (found) {
      if (
        this.environment !== undefined &&
        this.environment.Details !== undefined
      ) {
        this.environment.Details.EnvironmentOwner = found.DisplayName;
        this.environment.Details.EnvironmentOwnerId = found.Pid ?? found.Sid;

        if (!this.addMode) {
          const api = new RefDataEnvironmentsUsersApi();
          api
            .refDataEnvironmentsUsersOwnerIdPut({
              id: this.environment.EnvironmentId ?? 0,
              environmentOwnerApiModel: { DisplayName: this.selectedUser }
            })
            .subscribe({
              next: value => {
                if (value) {
                  this.setFoundOwnerLocally();
                  const event = new CustomEvent('environment-details-updated', {
                    detail: {},
                    bubbles: true,
                    composed: true
                  });
                  this.dispatchEvent(event);
                }
              },
              error: (err: any) => {
                console.error(err?.response?.ExceptionMessage);
                this.ErrorMessage = err?.response?.ExceptionMessage ?? err?.response;
              },
            });
        } else {
          this.setFoundOwnerLocally();
          this._inputValueChanged();
          this._canSubmit();
        }
      }
    }
  }

  searchResultsRenderer(
    root: HTMLElement,
    _comboBox: ComboBox,
    model: ComboBoxItemModel<UserElementApiModel>
  ) {
    render(
      html` <vaadin-vertical-layout>
        <div style="line-height: var(--lumo-line-height-m);">
          ${model.item.DisplayName ?? ''}
        </div>
        <div
          style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
        >
          ${model.item.Username ?? ''}
        </div>
      </vaadin-vertical-layout>`,
      root
    );
  }

  searchResultsValueChanged(data: CustomEvent<any>) {
    this.selectedUser = data.detail.value;
  }

  updateSearchCriteria(data: any) {
    this.searchADValue = data.currentTarget.value;
  }

  firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    const field = this.shadowRoot?.getElementById(
      'search-criteria'
    ) as TextField;
    field.addEventListener('keydown', this.isCriteriaReady as EventListener);

    this.addEventListener(
      'env-owner-search-criteria-ready',
      this.searchAD as EventListener
    );

    const api = new RefDataEnvironmentsApi();
    api.refDataEnvironmentsGetAllEnvironmentNamesGet().subscribe({
      next: (data: string[]) => {
        this.allEnvNames = data;
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done getting environment names')
    });
  }

  searchAD() {
    this.searchingUsers = true;
    const api = new RefDataEnvironmentsUsersApi();
    api
      .refDataEnvironmentsUsersSearchUsersSearchGet({
        search: this.searchADValue
      })
      .subscribe({
        next: (data: Array<UserElementApiModel>) => {
          this.searchResults = data;
          this.searchingUsers = false;
          const combo = this.shadowRoot?.getElementById(
            'searchResults'
          ) as ComboBox;
          if (combo) combo.open();
        },
        error: (err: any) => console.error(err),
        complete: () => console.log('Finished searching Active Directory')
      });
  }

  findDisplayNameForOwner() {
    if (
      this.environment !== undefined &&
      this.environment.Details?.EnvironmentOwner !== ''
    ) {
      if (
        this.EnvOwnerDisplayName === undefined ||
        this.EnvOwnerDisplayName === ''
      ) {
        this.EnvOwnerDisplayName = ' ';
        const api = new RefDataEnvironmentsUsersApi();
        api
          .refDataEnvironmentsUsersSearchUsersSearchGet({
            search: this.environment.Details?.EnvironmentOwner ?? ''
          })
          .subscribe({
            next: (data: Array<UserElementApiModel>) => {
                const user = data.length === 1 
                ? data[0] 
                : data.find(u => u.Pid === this.environment.Details?.EnvironmentOwnerId) ?? 
                  data.find(u => u.Sid === this.environment.Details?.EnvironmentOwnerId) ??
                  data.find(u => u.Username === this.environment.Details?.EnvironmentOwner) ??
                  data.find(u => u.DisplayName === this.environment.Details?.EnvironmentOwner);

              if (user)
                this.EnvOwnerDisplayName =
                  user.DisplayName !== null ? user.DisplayName : undefined;
            },
            error: (err: any) => {
              this.EnvOwnerDisplayName = '';
              console.error(err);
            },
            complete: () => console.log('Finished searching Active Directory')
          });
      }

      this._checkName(this.environment.EnvironmentName ?? '');
      this._inputValueChanged();
    }
  }

  updateSecure(e: CustomEvent) {
    const cbx = e.target as Checkbox;
    if (this.environment) {
      this.environment.EnvironmentSecure = cbx.checked;
    }
  }

  updateIsProd(e: CustomEvent) {
    const cbx = e.target as Checkbox;
    if (this.environment) {
      this.environment.EnvironmentIsProd = cbx.checked;
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

  _envNameValueChanged(data: any) {
    this.checkEnvironmentNameValid(data);
  }

  _descriptionValueChanged(data: any) {
    if (this.environment !== undefined && data.target !== undefined) {
      const model: EnvironmentApiModel = JSON.parse(
        JSON.stringify(this.environment)
      );
      if (model.Details) {
        model.Details.Description = data.target.value;
      }
      this.environment = model;
      this._inputValueChanged();
    }
  }

  _backupValueChanged(data: any) {
    if (this.environment !== undefined && data.target !== undefined) {
      const model: EnvironmentApiModel = JSON.parse(
        JSON.stringify(this.environment)
      );
      if (model.Details) {
        model.Details.RestoredFromSourceDb = data.target.value;
      }
      this.environment = model;
      this._inputValueChanged();
    }
  }

  _fileShareValueChanged(data: any) {
    if (this.environment !== undefined && data.target !== undefined) {
      const model: EnvironmentApiModel = JSON.parse(
        JSON.stringify(this.environment)
      );
      if (model.Details) {
        model.Details.FileShare = data.target.value;
      }
      this.environment = model;
      this._inputValueChanged();
    }
  }

  _thinClientValueChanged(data: any) {
    if (this.environment !== undefined && data.target !== undefined) {
      const model: EnvironmentApiModel = JSON.parse(
        JSON.stringify(this.environment)
      );
      if (model.Details) {
        model.Details.ThinClient = data.target.value;
      }
      this.environment = model;
      this._inputValueChanged();
    }
  }

  _notesValueChanged(data: any) {
    if (this.environment !== undefined && data.target !== undefined) {
      const model: EnvironmentApiModel = JSON.parse(
        JSON.stringify(this.environment)
      );
      if (model.Details) {
        model.Details.Notes = data.target.value;
      }
      this.environment = model;
      this._inputValueChanged();
    }
  }

  _checkName(data: string) {
    const found = this.allEnvNames?.find(name => name === data);
    // New environment check
    if (found === undefined && data.trim().length > 0) {
      this.isNameValid = true;
    }
    // Existing environment check
    else this.isNameValid = found !== undefined && data === found;

    this._canSubmit();
  }

  _inputValueChanged() {
    let result = true;
    if (this.environment !== undefined) {
      if (
        this.environment.Details?.EnvironmentOwner === 'NotSet' ||
        this.environment.Details?.EnvironmentOwner === ''
      ) {
        result = false;
      }
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
    this.canSubmit = this.envValid && this.isNameValid;
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
              this.envAdded();
              Notification.show(`Created Environment ${data.EnvironmentName}`, {
                theme: 'success',
                position: 'bottom-start',
                duration: 5000
              });
              this.savingMetadata = false;
            },
            error: (err: any) => {
              console.error(err.response);
              this.ErrorMessage = err.response;
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
              console.error(err.response);
              this.ErrorMessage = err.response;
              this.savingMetadata = false;
            },
            complete: () => console.log('done updating environment')
          });
      }
    }
  }

  envUpdated(data: EnvironmentApiModel) {
    const event = new CustomEvent('environment-details-updated', {
      detail: {
        environment: data
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  envAdded() {
    const event = new CustomEvent('environment-added', {
      detail: {
        environment: this.environment
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);

    this.Reset();
  }

  Reset() {
    this.environment = this.getEmptyEnv();

    this.canSubmit = false;
  }

  private setFoundOwnerLocally() {
    this.EnvOwnerDisplayName = this.selectedUser;
  }

  private isCriteriaReady(e: KeyboardEvent) {
    if (e.code === 'Enter') {
      const event = new CustomEvent('env-owner-search-criteria-ready', {
        detail: {
          message: 'Environment Owner Search Criteria Ready!'
        },
        bubbles: true,
        composed: true
      });
      this.dispatchEvent(event);
    }
  }

  private checkEnvironmentNameValid(data: any) {
    if (this.environment !== undefined && data.target !== undefined) {
      const environmentName = data.target.value as string;
      this.environment.EnvironmentName = environmentName.trim();

      this._checkName(this.environment.EnvironmentName);
    }
  }
}
