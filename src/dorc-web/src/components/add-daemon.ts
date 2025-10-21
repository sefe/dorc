import { css, LitElement } from 'lit';
import '@vaadin/text-field';
import type { TextField } from '@vaadin/text-field';
import '@vaadin/button';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import type { DaemonApiModel } from '../apis/dorc-api';
import { RefDataDaemonsApi } from '../apis/dorc-api';

@customElement('add-daemon')
export class AddDaemon extends LitElement {
  @state() private displayName = '';

  @state() private displayNameValid = false;

  @state() private daemonName = '';

  @state() private daemonNameValid = false;

  @property() private accountName = this.getEmptyDaemon().AccountName;

  @property({ type: Boolean }) private accountNameValid = true;

  @property() private serviceType = this.getEmptyDaemon().ServiceType;

  @property({ type: Boolean }) private serviceTypeValid = true;

  @property({ type: Boolean }) private valid = false;

  @property({ type: Boolean }) private isBusy = false;

  @property({ type: Object })
  private daemon: DaemonApiModel = this.getEmptyDaemon();

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
            id="daemon-name"
            label="Daemon Name"
            required
            auto-validate
            @input="${this._daemonNameValueChanged}"
            .value="${this.daemonName}"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            id="display-name"
            label="Display Name"
            required
            auto-validate
            @input="${this._displayNameValueChanged}"
            .value="${this.displayName}"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            id="account-name"
            label="Account Name"
            required
            auto-validate
            @input="${this._accountNameValueChanged}"
            .value="${this.accountName ?? ''}"
            .readonly="${true}"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            id="service-type"
            label="Type"
            required
            auto-validate
            @input="${this._serviceTypeValueChanged}"
            .value="${this.serviceType ?? ''}"
            .readonly="${true}"
          >
          </vaadin-text-field>
        </vaadin-vertical-layout>
        <div>
      <vaadin-button
        .disabled="${!this.valid || this.isBusy}"
        @click="${this._submit}"
      >
        Save
      </vaadin-button>
      <vaadin-button @click="${this.reset}">Clear</vaadin-button>
      </div>
        <span style="color: darkred">${this.overlayMessage}</span>
      </div>
    `;
  }

  _displayNameValueChanged(data: any) {
    this.displayName = data.currentTarget.value;
    this.displayNameValid = this.displayName.length > 0;
    this.validate();
  }

  _daemonNameValueChanged(data: any) {
    this.daemonName = data.currentTarget.value;
    this.daemonNameValid = this.daemonName.length > 0;
    this.validate();
  }

  _accountNameValueChanged(data: any) {
    this.accountName = data.currentTarget.value;
    if (this.accountName !== undefined) {
      this.accountNameValid = (this.accountName?.length ?? 0) > 0;
    }
    this.validate();
  }

  _serviceTypeValueChanged(data: any) {
    this.serviceType = data.currentTarget.value;
    if (this.serviceType !== undefined) {
      this.serviceTypeValid = (this.serviceType?.length ?? 0) > 0;
    }
    this.validate();
  }

  validate() {
    if (this.daemon !== undefined) {
      if (
        this.daemonNameValid &&
        this.displayNameValid &&
        this.accountNameValid &&
        this.serviceTypeValid
      ) {
        this.valid = true;
      } else {
        this.valid = false;
      }
    }
  }

  _submit() {
    this.isBusy = true;
    const api = new RefDataDaemonsApi();

    this.daemon.AccountName = this.accountName;
    this.daemon.DisplayName = this.displayName;
    this.daemon.ServiceType = this.serviceType;
    this.daemon.Name = this.daemonName;

    api.refDataDaemonsPost({ daemonApiModel: this.daemon }).subscribe(
      (data: DaemonApiModel) => {
        this._addDaemon(data);
      },
      (err: any) => {
        this.isBusy = false;
        this.overlayMessage = 'Error creating daemon!';
        console.error(err);
      },
      () => {
        console.log('done adding daemon');
        this.reset();
      }
    );
  }

  _addDaemon(data: DaemonApiModel) {
    if (data.Id !== 0) {
      const event = new CustomEvent('daemon-created', {
        detail: {
          daemon: this.daemon
        },
        bubbles: true,
        composed: true
      });
      this.dispatchEvent(event);
    } else {
      this.overlayMessage = 'Error adding daemon!';
    }
  }

clearTextField(name: string) {
  const element = this.shadowRoot?.getElementById(name) as TextField | null;
  if (element) {
    element.value = '';
  }
}

  reset() {
    this.clearTextField('daemon-name');
    this.clearTextField('display-name');

    this.daemon = this.getEmptyDaemon();
    this.displayNameValid = false;
    this.daemonNameValid = false;

    this.valid = false;
    this.isBusy = false;
    this.overlayMessage = '';
  }

  getEmptyDaemon(): DaemonApiModel {
    const domain: DaemonApiModel = {
      DisplayName: '',
      ServiceType: 'Windows Service',
      Name: '',
      AccountName: 'Local System Account'
    };
    return domain;
  }
}
