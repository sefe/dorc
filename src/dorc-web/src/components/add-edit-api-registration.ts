import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/text-field';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import {
  ApiRegistrationApiModel,
  RefDataApiRegistrationsApi
} from '../apis/dorc-api';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { confirmSharedResourceChange } from '../helpers/shared-resource-confirm';

/**
 * Create/edit form for an API registration. Fires `api-registration-saved`.
 * When `envId` is set, a newly created registration is attached to that
 * environment as part of the save.
 */
@customElement('add-edit-api-registration')
export class AddEditApiRegistration extends LitElement {
  @property({ type: Number }) envId = 0;

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
        placeholder="e.g. trade-capture-api"
        maxlength="${this.maxNameLength}"
        .value="${this._apiRegistration.Name ?? ''}"
        @input="${(e: Event) => this.setField('Name', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Base URL"
        required
        placeholder="e.g. https://trade-capture-api.ut2.example.com"
        maxlength="${this.maxUrlLength}"
        .value="${this._apiRegistration.BaseUrl ?? ''}"
        @input="${(e: Event) => this.setField('BaseUrl', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Version"
        placeholder="e.g. v2"
        maxlength="${this.maxVersionLength}"
        .value="${this._apiRegistration.Version ?? ''}"
        @input="${(e: Event) => this.setField('Version', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Health Check URL"
        placeholder="e.g. https://trade-capture-api.ut2.example.com/health"
        maxlength="${this.maxUrlLength}"
        .value="${this._apiRegistration.HealthCheckUrl ?? ''}"
        @input="${(e: Event) => this.setField('HealthCheckUrl', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Tags"
        placeholder="e.g. rest;internal"
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

  private async save() {
    const apiRegistration = { ...this._apiRegistration };
    const envId = this.envId;
    if (!apiRegistration.Name || !apiRegistration.BaseUrl) {
      Notification.show('Name and Base URL are required', {
        theme: 'error',
        position: 'bottom-start',
        duration: 5000
      });
      return;
    }

    const api = new RefDataApiRegistrationsApi(dorcApiConfiguration);
    const isUpdate = !!apiRegistration.Id;
    if (
      isUpdate &&
      !(await confirmSharedResourceChange(apiRegistration, 'edit'))
    )
      return;
    const request = isUpdate
      ? api.refDataApiRegistrationsIdPut({
          id: apiRegistration.Id!,
          apiRegistrationApiModel: apiRegistration
        })
      : api.refDataApiRegistrationsPost({
          apiRegistrationApiModel: apiRegistration
        });

    request.subscribe({
      next: (saved: ApiRegistrationApiModel) => {
        if (!isUpdate && envId > 0 && saved?.Id) {
          this.attachToEnvironment(api, saved, envId);
          return;
        }
        Notification.show(
          `API registration ${apiRegistration.Name} ${isUpdate ? 'updated' : 'created'}`,
          { theme: 'success', position: 'bottom-start', duration: 5000 }
        );
        this.fireSaved();
      },
      error: (err: any) => {
        Notification.show(
          `Failed to save API registration: ${err.response ?? err.message ?? err}`,
          { theme: 'error', position: 'bottom-start', duration: 5000 }
        );
      }
    });
  }

  private attachToEnvironment(
    api: RefDataApiRegistrationsApi,
    saved: ApiRegistrationApiModel,
    envId: number
  ) {
    api
      .refDataApiRegistrationsIdEnvironmentsEnvIdPut({
        id: saved.Id!,
        envId
      })
      .subscribe({
        next: () => {
          Notification.show(
            `API registration ${saved.Name} created and attached to this environment`,
            { theme: 'success', position: 'bottom-start', duration: 5000 }
          );
          this.fireSaved();
        },
        error: (err: any) => {
          Notification.show(
            `API registration ${saved.Name} was created but could not be attached: ${err.response ?? err.message ?? err}`,
            { theme: 'error', position: 'bottom-start', duration: 5000 }
          );
          this.fireSaved();
        }
      });
  }

  private fireSaved() {
    this.dispatchEvent(
      new CustomEvent('api-registration-saved', {
        bubbles: true,
        composed: true
      })
    );
  }
}
