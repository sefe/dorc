import { css, LitElement } from 'lit';
import '@vaadin/text-field';
import '@vaadin/checkbox';
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

@customElement('add-config-value')
export class AddConfigValue extends LitElement {
  @state() private key = '';

  @state() private keyValid = false;

  @state() private value = '';

  @state() private valueValid = false;

  @property({ type: Boolean }) private isSecure = false;

  @property({ type: Boolean }) private isForProd: boolean = false;

  @property({ type: Boolean }) private valid = false;

  @property({ type: Object })
  private configValue: ConfigValueApiModel = this.getEmptyConfigValue();

  @property() private overlayMessage: any;

  static get styles() {
    return css`
      .block {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 500px;
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
      <div style="width:50%;">
        <vaadin-vertical-layout>
          <vaadin-text-field
            class="block"
            id="key"
            label="Config Key"
            required
            @input="${this._configKeyValueChanged}"
            .value="${this.key}"
            tabindex="1"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            id="value"
            label="Config Value"
            required
            @input="${this._configValueValueChanged}"
            .value="${this.value}"
            tabindex="2"
          ></vaadin-text-field>
          <vaadin-checkbox
            id="secure"
            label="Is Secure"
            @change="${(e: Event) => {
              this.isSecure = (e.target as HTMLInputElement).checked;
            }}"
            tabindex="3"
          ></vaadin-checkbox>
          <vaadin-checkbox
            id="for-prod"
            label="Is For Prod"
            @change="${(e: Event) => {
              this.isForProd = (e.target as HTMLInputElement).checked;
            }}"
            tabindex="3"
          ></vaadin-checkbox>
          </vaadin-text-field>
        </vaadin-vertical-layout>
        <div>
          <vaadin-button @click="${this.reset}">Clear</vaadin-button>
          <vaadin-button .disabled="${!this.valid}" @click="${this._submit}" tabindex="4"
          >Save
          </vaadin-button
          >
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
    const api = new RefDataConfigApi();

    this.configValue.Secure = this.isSecure;
    this.configValue.IsForProd = this.isForProd;
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
      IsForProd: undefined
    };
  }
}
