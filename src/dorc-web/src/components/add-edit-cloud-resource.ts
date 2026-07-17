import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/text-field';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import { CloudResourceApiModel, RefDataCloudResourcesApi } from '../apis/dorc-api';

/**
 * Create/edit form for a cloud resource definition. Fires `cloud-resource-saved`.
 */
@customElement('add-edit-cloud-resource')
export class AddEditCloudResource extends LitElement {
  @property({ type: Object })
  get cloudResource(): CloudResourceApiModel {
    return this._cloudResource;
  }

  set cloudResource(value: CloudResourceApiModel) {
    const oldVal = this._cloudResource;
    this._cloudResource = { ...value };
    this.requestUpdate('cloudResource', oldVal);
  }

  private _cloudResource: CloudResourceApiModel = {};

  private readonly maxFieldLength = 250;
  private readonly maxIdentifierLength = 500;

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
        maxlength="${this.maxFieldLength}"
        .value="${this._cloudResource.Name ?? ''}"
        @input="${(e: Event) => this.setField('Name', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Provider"
        required
        maxlength="${this.maxFieldLength}"
        .value="${this._cloudResource.Provider ?? ''}"
        @input="${(e: Event) => this.setField('Provider', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Resource Type"
        required
        maxlength="${this.maxFieldLength}"
        .value="${this._cloudResource.ResourceType ?? ''}"
        @input="${(e: Event) => this.setField('ResourceType', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Resource Identifier"
        required
        maxlength="${this.maxIdentifierLength}"
        .value="${this._cloudResource.ResourceIdentifier ?? ''}"
        @input="${(e: Event) => this.setField('ResourceIdentifier', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Subscription"
        maxlength="${this.maxFieldLength}"
        .value="${this._cloudResource.Subscription ?? ''}"
        @input="${(e: Event) => this.setField('Subscription', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Tags"
        helper-text="Semicolon-separated"
        maxlength="${this.maxFieldLength}"
        .value="${this._cloudResource.Tags ?? ''}"
        @input="${(e: Event) => this.setField('Tags', e)}"
      ></vaadin-text-field>
      <vaadin-button theme="primary" @click="${this.save}">
        ${this._cloudResource.Id ? 'Save' : 'Create'}
      </vaadin-button>
    `;
  }

  private setField(field: keyof CloudResourceApiModel, e: Event) {
    const value = (e.currentTarget as HTMLInputElement).value;
    this._cloudResource = { ...this._cloudResource, [field]: value };
  }

  private save() {
    if (
      !this._cloudResource.Name ||
      !this._cloudResource.Provider ||
      !this._cloudResource.ResourceType ||
      !this._cloudResource.ResourceIdentifier
    ) {
      Notification.show('Name, Provider, Resource Type and Resource Identifier are required', {
        theme: 'error',
        position: 'bottom-start',
        duration: 5000
      });
      return;
    }

    const api = new RefDataCloudResourcesApi();
    const request = this._cloudResource.Id
      ? api.refDataCloudResourcesIdPut({
          id: this._cloudResource.Id,
          cloudResourceApiModel: this._cloudResource
        })
      : api.refDataCloudResourcesPost({ cloudResourceApiModel: this._cloudResource });

    request.subscribe({
      next: () => {
        Notification.show(
          `Cloud resource ${this._cloudResource.Name} ${this._cloudResource.Id ? 'updated' : 'created'}`,
          { theme: 'success', position: 'bottom-start', duration: 5000 }
        );
        this.dispatchEvent(
          new CustomEvent('cloud-resource-saved', { bubbles: true, composed: true })
        );
      },
      error: (err: any) => {
        Notification.show(
          `Failed to save cloud resource: ${err.response ?? err.message ?? err}`,
          { theme: 'error', position: 'bottom-start', duration: 5000 }
        );
      }
    });
  }
}
