import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/text-field';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import { ApiRegistrationApiModel, RefDataApiRegistrationsApi } from '../apis/dorc-api';

/**
 * Create/edit form for an API registration. Fires `api-registration-saved`.
 */
@customElement('add-edit-api-registration')
export class AddEditApiRegistration extends LitElement {
  @property({ type: Object })
  get apiRegistration(): ApiRegistrationApiModel {
    return this._apiRegistration;
  }

  set apiRegistration(value: ApiRegistrationApiModel) {
    const oldVal = this._apiRegistration;
    this._apiRegistration = { ...value };
    this.requestUpdate('apiRegistration', oldVal);
  }

  private _apiRegistration: ApiRegistrationApiModel = {};

  private readonly maxNameLength = 250;
  private readonly maxUrlLength = 500;
  private readonly maxVersionLength = 50;
  private readonly maxTagsLength = 250;

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
      }
      vaadin-text-field {
        width: 320px;
      }
    `;
  }

  render() {
    return html`
      <vaadin-text-field
        label="Name"
        required
        maxlength="${this.maxNameLength}"
        .value="${this._apiRegistration.Name ?? ''}"
        @input="${(e: Event) => this.setField('Name', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Base URL"
        required
        maxlength="${this.maxUrlLength}"
        .value="${this._apiRegistration.BaseUrl ?? ''}"
        @input="${(e: Event) => this.setField('BaseUrl', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Version"
        maxlength="${this.maxVersionLength}"
        .value="${this._apiRegistration.Version ?? ''}"
        @input="${(e: Event) => this.setField('Version', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Health Check URL"
        maxlength="${this.maxUrlLength}"
        .value="${this._apiRegistration.HealthCheckUrl ?? ''}"
        @input="${(e: Event) => this.setField('HealthCheckUrl', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Tags"
        helper-text="Semicolon-separated"
        maxlength="${this.maxTagsLength}"
        .value="${this._apiRegistration.Tags ?? ''}"
        @input="${(e: Event) => this.setField('Tags', e)}"
      ></vaadin-text-field>
      <vaadin-button theme="primary" @click="${this.save}">
        ${this._apiRegistration.Id ? 'Save' : 'Create'}
      </vaadin-button>
    `;
  }

  private setField(field: keyof ApiRegistrationApiModel, e: Event) {
    const value = (e.currentTarget as HTMLInputElement).value;
    this._apiRegistration = { ...this._apiRegistration, [field]: value };
  }

  private save() {
    if (!this._apiRegistration.Name || !this._apiRegistration.BaseUrl) {
      Notification.show('Name and Base URL are required', {
        theme: 'error',
        position: 'bottom-start',
        duration: 5000
      });
      return;
    }

    const api = new RefDataApiRegistrationsApi();
    const request = this._apiRegistration.Id
      ? api.refDataApiRegistrationsIdPut({
          id: this._apiRegistration.Id,
          apiRegistrationApiModel: this._apiRegistration
        })
      : api.refDataApiRegistrationsPost({ apiRegistrationApiModel: this._apiRegistration });

    request.subscribe({
      next: () => {
        Notification.show(
          `API registration ${this._apiRegistration.Name} ${this._apiRegistration.Id ? 'updated' : 'created'}`,
          { theme: 'success', position: 'bottom-start', duration: 5000 }
        );
        this.dispatchEvent(
          new CustomEvent('api-registration-saved', { bubbles: true, composed: true })
        );
      },
      error: (err: any) => {
        Notification.show(
          `Failed to save API registration: ${err.response ?? err.message ?? err}`,
          { theme: 'error', position: 'bottom-start', duration: 5000 }
        );
      }
    });
  }
}
