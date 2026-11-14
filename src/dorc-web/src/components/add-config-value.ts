import { css, LitElement } from 'lit';
import { live } from 'lit/directives/live.js';
import '@vaadin/text-field';
import '@vaadin/checkbox';
import { Checkbox } from '@vaadin/checkbox';
import { TextField } from '@vaadin/text-field';
import '@vaadin/button';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import {
  ConfigValueApiModel,
  DaemonApiModel,
  RefDataConfigApi
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import '@vaadin/vertical-layout';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

@customElement('add-config-value')
export class AddConfigValue extends LitElement {
  @state() private key = '';

  @state() private keyValid = false;

  @state() private value = '';

  @state() private valueValid = false;

  @property({ type: Boolean }) isSecure = false;

  @property({ type: Boolean }) isForProd: boolean = false;

  @property({ type: Boolean }) valid = false;

  // Unticked by default, and only meaningful for a secure value: the server hides a new
  // secure value from script scope unless its creator says otherwise, while a non-secure
  // value is ordinary configuration and stays visible whatever this says. Defaulting the
  // other way would make every newly-added credential readable by every deployment
  // script the moment it was created.
  @property({ type: Boolean }) private visibleToScripts = false;

  @property({ type: Object }) configValue: ConfigValueApiModel =
    this.getEmptyConfigValue();

  @property() overlayMessage: any;

  static get styles() {
    return css`
      .block {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 100%;
        max-width: 500px;
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
    `;
  }

  render() {
    return html`
      <div
        style="padding: var(--lumo-space-s); width: min(500px, calc(100vw - 64px)); box-sizing: border-box;"
      >
        <vaadin-vertical-layout>
          <vaadin-text-field
            class="block"
            id="key"
            label="Config Key"
            required
            @input="${this._configKeyValueChanged}"
            .value="${this.key}"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            id="value"
            label="Config Value"
            required
            @input="${this._configValueValueChanged}"
            .value="${this.value}"
          ></vaadin-text-field>
          <vaadin-checkbox
            id="secure"
            label="Is Secure"
            .checked="${live(this.isSecure)}"
            @change="${(e: Event) => {
              this.isSecure = (e.currentTarget as Checkbox).checked;
              if (!this.isSecure) {
                this.visibleToScripts = false;
              }
            }}"
          ></vaadin-checkbox>
          <vaadin-checkbox
            id="for-prod"
            label="Is For Prod"
            .checked="${live(this.isForProd)}"
            @change="${(e: Event) => {
              this.isForProd = (e.currentTarget as Checkbox).checked;
            }}"
          ></vaadin-checkbox>
          <vaadin-checkbox
            id="visible-to-scripts"
            label="Visible To Scripts"
            title="Allow deployment scripts to read this secure value. Non-secure values are visible to scripts unless reserved; reserved DOrc operating credentials are always withheld from script scope regardless of this setting."
            ?disabled="${!this.isSecure}"
            .checked="${live(this.visibleToScripts)}"
            @change="${(e: Event) => {
              this.visibleToScripts = (e.currentTarget as Checkbox).checked;
            }}"
          ></vaadin-checkbox>
        </vaadin-vertical-layout>
        <div>
          <vaadin-button @click="${this.reset}">Clear</vaadin-button>
          <vaadin-button .disabled="${!this.valid}" @click="${this._submit}"
            >Save
          </vaadin-button>
        </div>
        <span style="color: darkred">${this.overlayMessage}</span>
      </div>
    `;
  }

  _configValueValueChanged(data: any) {
    this.value = data.currentTarget.value;
    this.valueValid = this.value.length > 0;
    this.validate();
  }

  _configKeyValueChanged(data: any) {
    this.key = data.currentTarget.value;
    this.keyValid = this.key.length > 0;
    this.validate();
  }

  validate() {
    if (this.configValue !== undefined) {
      this.valid = (this.keyValid && this.valueValid) ?? false;
    }
  }

  _submit() {
    const api = new RefDataConfigApi(dorcApiConfiguration);

    this.configValue.Secure = this.isSecure;
    this.configValue.IsForProd = this.isForProd;
    this.configValue.VisibleToScripts = this.isSecure
      ? this.visibleToScripts
      : false;
    this.configValue.Key = this.key;
    this.configValue.Value = this.value;

    api.refDataConfigPost({ configValueApiModel: this.configValue }).subscribe({
      next: (data: DaemonApiModel) => {
        this._addConfigValue(data);
      },
      error: (err: any) => {
        const errMessage = retrieveErrorMessage(err);
        this.overlayMessage = errMessage;
        console.error(err);
      },
      complete: () => {
        console.log('done adding configValue');
        this.reset();
      }
    });
  }

  _addConfigValue(data: DaemonApiModel) {
    if (data.Id !== 0) {
      const event = new CustomEvent('config-value-created', {
        detail: {
          configValue: this.configValue
        },
        bubbles: true,
        composed: true
      });
      this.dispatchEvent(event);
    } else {
      this.overlayMessage = 'Error adding configValue!';
    }
  }

  clearTextField(name: string) {
    const TextFieldValue = this.shadowRoot?.getElementById(name) as TextField;
    if (TextFieldValue) {
      TextFieldValue.value = '';
    }
  }

  reset() {
    this.clearTextField('key');
    this.clearTextField('value');

    this.key = '';
    this.value = '';
    this.isSecure = false;
    this.isForProd = false;
    this.visibleToScripts = false;
    this.configValue = this.getEmptyConfigValue();
    this.keyValid = false;
    this.valueValid = false;

    this.valid = false;
    this.overlayMessage = '';
  }

  getEmptyConfigValue(): ConfigValueApiModel {
    return {
      Key: '',
      Value: '',
      Secure: false,
      IsForProd: undefined,
      VisibleToScripts: false
    };
  }
}
