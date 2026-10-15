import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/text-field';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import { ContainerApiModel, RefDataContainersApi } from '../apis/dorc-api';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

/**
 * Create/edit form for a container definition. Fires `container-saved` on success.
 * When `envId` is set, a newly created container is attached to that
 * environment as part of the save.
 */
@customElement('add-edit-container')
export class AddEditContainer extends LitElement {
  @property({ type: Number }) envId = 0;

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
        placeholder="e.g. trade-ingest-worker"
        maxlength="${this.maxNameLength}"
        .value="${this._container.Name ?? ''}"
        @input="${(e: Event) => this.setField('Name', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Image"
        required
        placeholder="e.g. trade-ingest:1.4.2"
        helper-text="Image name and tag"
        maxlength="${this.maxImageLength}"
        .value="${this._container.Image ?? ''}"
        @input="${(e: Event) => this.setField('Image', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Registry"
        placeholder="e.g. myregistry.azurecr.io"
        maxlength="${this.maxFieldLength}"
        .value="${this._container.Registry ?? ''}"
        @input="${(e: Event) => this.setField('Registry', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Host Server Name"
        placeholder="e.g. aks-cluster-ut2"
        helper-text="Server or cluster hosting the container"
        maxlength="${this.maxFieldLength}"
        .value="${this._container.HostServerName ?? ''}"
        @input="${(e: Event) => this.setField('HostServerName', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Tags"
        placeholder="e.g. ingest;web tier"
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

    const api = new RefDataContainersApi(dorcApiConfiguration);
    const isUpdate = !!this._container.Id;
    const request = isUpdate
      ? api.refDataContainersIdPut({
          id: this._container.Id!,
          containerApiModel: this._container
        })
      : api.refDataContainersPost({ containerApiModel: this._container });

    request.subscribe({
      next: (saved: ContainerApiModel) => {
        if (!isUpdate && this.envId > 0 && saved?.Id) {
          this.attachToEnvironment(api, saved);
          return;
        }
        Notification.show(
          `Container ${this._container.Name} ${isUpdate ? 'updated' : 'created'}`,
          { theme: 'success', position: 'bottom-start', duration: 5000 }
        );
        this.fireSaved();
      },
      error: (err: any) => {
        Notification.show(
          `Failed to save container: ${err.response ?? err.message ?? err}`,
          { theme: 'error', position: 'bottom-start', duration: 5000 }
        );
      }
    });
  }

  private attachToEnvironment(
    api: RefDataContainersApi,
    saved: ContainerApiModel
  ) {
    api
      .refDataContainersIdEnvironmentsEnvIdPut({
        id: saved.Id!,
        envId: this.envId
      })
      .subscribe({
        next: () => {
          Notification.show(
            `Container ${saved.Name} created and attached to this environment`,
            { theme: 'success', position: 'bottom-start', duration: 5000 }
          );
          this.fireSaved();
        },
        error: (err: any) => {
          Notification.show(
            `Container ${saved.Name} was created but could not be attached: ${err.response ?? err.message ?? err}`,
            { theme: 'error', position: 'bottom-start', duration: 5000 }
          );
          this.fireSaved();
        }
      });
  }

  private fireSaved() {
    this.dispatchEvent(
      new CustomEvent('container-saved', { bubbles: true, composed: true })
    );
  }
}
