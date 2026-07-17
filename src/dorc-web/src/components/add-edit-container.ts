import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/text-field';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import { ContainerApiModel, RefDataContainersApi } from '../apis/dorc-api';

/**
 * Create/edit form for a container definition. Fires `container-saved` on success.
 */
@customElement('add-edit-container')
export class AddEditContainer extends LitElement {
  @property({ type: Object })
  get container(): ContainerApiModel {
    return this._container;
  }

  set container(value: ContainerApiModel) {
    const oldVal = this._container;
    this._container = { ...value };
    this.requestUpdate('container', oldVal);
  }

  private _container: ContainerApiModel = {};

  private readonly maxNameLength = 250;
  private readonly maxImageLength = 500;
  private readonly maxFieldLength = 250;

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
        .value="${this._container.Name ?? ''}"
        @input="${(e: Event) => this.setField('Name', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Image"
        required
        maxlength="${this.maxImageLength}"
        .value="${this._container.Image ?? ''}"
        @input="${(e: Event) => this.setField('Image', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Registry"
        maxlength="${this.maxFieldLength}"
        .value="${this._container.Registry ?? ''}"
        @input="${(e: Event) => this.setField('Registry', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Host Server Name"
        maxlength="${this.maxFieldLength}"
        .value="${this._container.HostServerName ?? ''}"
        @input="${(e: Event) => this.setField('HostServerName', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Tags"
        helper-text="Semicolon-separated"
        maxlength="${this.maxFieldLength}"
        .value="${this._container.Tags ?? ''}"
        @input="${(e: Event) => this.setField('Tags', e)}"
      ></vaadin-text-field>
      <vaadin-button theme="primary" @click="${this.save}">
        ${this._container.Id ? 'Save' : 'Create'}
      </vaadin-button>
    `;
  }

  private setField(field: keyof ContainerApiModel, e: Event) {
    const value = (e.currentTarget as HTMLInputElement).value;
    this._container = { ...this._container, [field]: value };
  }

  private save() {
    if (!this._container.Name || !this._container.Image) {
      Notification.show('Name and Image are required', {
        theme: 'error',
        position: 'bottom-start',
        duration: 5000
      });
      return;
    }

    const api = new RefDataContainersApi();
    const request = this._container.Id
      ? api.refDataContainersIdPut({
          id: this._container.Id,
          containerApiModel: this._container
        })
      : api.refDataContainersPost({ containerApiModel: this._container });

    request.subscribe({
      next: () => {
        Notification.show(
          `Container ${this._container.Name} ${this._container.Id ? 'updated' : 'created'}`,
          { theme: 'success', position: 'bottom-start', duration: 5000 }
        );
        this.dispatchEvent(
          new CustomEvent('container-saved', { bubbles: true, composed: true })
        );
      },
      error: (err: any) => {
        Notification.show(
          `Failed to save container: ${err.response ?? err.message ?? err}`,
          { theme: 'error', position: 'bottom-start', duration: 5000 }
        );
      }
    });
  }
}
