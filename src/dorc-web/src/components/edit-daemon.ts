import { css, LitElement } from 'lit';
import '@vaadin/text-field';
import '@vaadin/button';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import type { DaemonApiModel } from '../apis/dorc-api';
import { RefDataDaemonsApi } from '../apis/dorc-api';

@customElement('edit-daemon')
export class EditDaemon extends LitElement {
  private readonly maxFieldLength = 250;

  @property({ type: Object })
  daemon: DaemonApiModel = this.getEmptyDaemon();

  @state() private daemonName = '';
  @state() private displayName = '';
  @state() private accountName = '';
  @state() private serviceType = '';

  @property({ type: Boolean }) private isBusy = false;
  @property() private overlayMessage: any = '';

  static get styles() {
    return css`
      .block {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 500px;
      }
    `;
  }

  protected updated(changed: Map<string, unknown>) {
    if (changed.has('daemon') && this.daemon) {
      this.daemonName = this.daemon.Name ?? '';
      this.displayName = this.daemon.DisplayName ?? '';
      this.accountName = this.daemon.AccountName ?? '';
      this.serviceType = this.daemon.ServiceType ?? '';
    }
  }

  render() {
    return html`
      <div style="width:50%;">
        <vaadin-vertical-layout>
          <vaadin-text-field
            class="block"
            id="edit-daemon-name"
            label="Daemon Name"
            maxlength="${this.maxFieldLength}"
            title="Maximum length: ${this.maxFieldLength} symbols"
            required
            @input="${(e: any) => { this.daemonName = e.currentTarget.value; }}"
            .value="${this.daemonName}"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            id="edit-display-name"
            label="Display Name"
            maxlength="${this.maxFieldLength}"
            title="Maximum length: ${this.maxFieldLength} symbols"
            required
            @input="${(e: any) => { this.displayName = e.currentTarget.value; }}"
            .value="${this.displayName}"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            id="edit-account-name"
            label="Account Name"
            maxlength="${this.maxFieldLength}"
            title="Maximum length: ${this.maxFieldLength} symbols"
            required
            @input="${(e: any) => { this.accountName = e.currentTarget.value; }}"
            .value="${this.accountName}"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            id="edit-service-type"
            label="Type"
            maxlength="${this.maxFieldLength}"
            title="Maximum length: ${this.maxFieldLength} symbols"
            required
            @input="${(e: any) => { this.serviceType = e.currentTarget.value; }}"
            .value="${this.serviceType}"
          ></vaadin-text-field>
        </vaadin-vertical-layout>
        <div>
          <vaadin-button
            .disabled="${this.isBusy}"
            @click="${this._submit}"
          >Save</vaadin-button>
        </div>
        <span style="color: darkred">${this.overlayMessage}</span>
      </div>
    `;
  }

  _submit() {
    if (!this.daemon.Id) {
      this.overlayMessage = 'Missing daemon id';
      return;
    }
    this.isBusy = true;
    this.overlayMessage = '';

    const payload: DaemonApiModel = {
      Id: this.daemon.Id,
      Name: this.daemonName,
      DisplayName: this.displayName,
      AccountName: this.accountName,
      ServiceType: this.serviceType
    };

    const api = new RefDataDaemonsApi();
    api.refDataDaemonsPut({ id: this.daemon.Id, daemonApiModel: payload }).subscribe(
      (data: DaemonApiModel) => {
        this.isBusy = false;
        this.dispatchEvent(new CustomEvent('daemon-updated', {
          detail: { daemon: data },
          bubbles: true,
          composed: true
        }));
      },
      (err: any) => {
        this.isBusy = false;
        this.overlayMessage = this._extractErrorMessage(err) ?? 'Error updating daemon';
      }
    );
  }

  private _extractErrorMessage(err: any): string | null {
    if (err?.response) {
      if (typeof err.response === 'string') return err.response;
      if (typeof err.response.ExceptionMessage === 'string') return err.response.ExceptionMessage;
      if (typeof err.response.message === 'string') return err.response.message;
    }
    if (err?.message) return err.message;
    return null;
  }

  private getEmptyDaemon(): DaemonApiModel {
    return {
      Id: 0,
      Name: '',
      DisplayName: '',
      AccountName: '',
      ServiceType: ''
    };
  }
}
