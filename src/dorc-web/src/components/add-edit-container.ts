import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/text-field';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import { ContainerApiModel, RefDataContainersApi } from '../apis/dorc-api';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { confirmSharedResourceChange } from '../helpers/shared-resource-confirm';
import { splitTags } from '../helpers/tag-parser';
import { resolveResourceTagTokens } from './resource-tags';

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
      .token-hints {
        width: 320px;
        box-sizing: border-box;
        margin: var(--lumo-space-xs, 4px) 0 8px;
        padding: 6px 10px;
        border-radius: var(--lumo-border-radius-m, 6px);
        background: var(--dorc-bg-secondary, rgba(128, 128, 128, 0.08));
        border: 1px solid var(--dorc-border-color, rgba(128, 128, 128, 0.2));
        font-size: var(--lumo-font-size-xs, 0.78rem);
        color: var(--lumo-secondary-text-color);
      }
      .token-hints-label {
        display: block;
        font-weight: 600;
        margin-bottom: 4px;
        color: var(--lumo-body-text-color);
      }
      .token-chip-list {
        display: flex;
        flex-wrap: wrap;
        gap: 6px;
        align-items: center;
      }
      .token-chip {
        display: inline-flex;
        align-items: center;
        padding: 2px 8px;
        border-radius: 999px;
        border: 1px solid var(--dorc-border-color, rgba(128, 128, 128, 0.28));
        background: var(--dorc-chip-bg, rgba(59, 130, 246, 0.12));
        color: var(--dorc-chip-text, inherit);
        font-family: var(--lumo-font-family-monospace, monospace);
        font-size: var(--lumo-font-size-xs, 0.78rem);
        cursor: pointer;
      }
    `;
  }

  private async copyToken(token: string) {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(token);
      Notification.show(`Copied ${token} to clipboard`, {
        theme: 'contrast',
        position: 'bottom-start',
        duration: 2500
      });
    }
  }

  private renderTokenHints() {
    const { tokens, conflicts } = resolveResourceTagTokens(
      'ContainerNames_',
      splitTags(this._container.Tags)
    );
    return html`
      <div
        class="token-hints"
        role="region"
        aria-label="Exposed container tag tokens"
      >
        <span class="token-hints-label">
          Built-in variable token${tokens.length === 1 ? '' : 's'} exposed by
          container tags:
        </span>
        ${
          tokens.length > 0
            ? html`
                <div class="token-chip-list">
                  ${tokens.map(
                    token => html`
                      <button
                        type="button"
                        class="token-chip"
                        title="Click to copy ${token}"
                        @click="${() => this.copyToken(token)}"
                      >
                        <code>${token}</code>
                      </button>
                    `
                  )}
                </div>
              `
            : html`<span class="token-empty-hint"
                >Add a tag (e.g. <code>ingest</code>) to expose
                <code>$ContainerNames_ingest$</code> in environment
                variables.</span
              >`
        }
        ${
          conflicts.length > 0
            ? html`<div class="token-conflict-hint" role="alert">
                Conflicting tags normalize to the same variable token:
                ${conflicts.join('; ')}. Rename one so the token can resolve.
              </div>`
            : ''
        }
      </div>
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
      ${this.renderTokenHints()}
      <vaadin-button theme="primary" @click="${this.save}">
        ${this._container.Id ? 'Save' : 'Create'}
      </vaadin-button>
    `;
  }

  private setField(field: keyof ContainerApiModel, e: Event) {
    const value = (e.currentTarget as HTMLInputElement).value;
    this._container = { ...this._container, [field]: value };
    this.requestUpdate();
  }

  private async save() {
    const container = { ...this._container };
    const envId = this.envId;
    if (!container.Name || !container.Image) {
      Notification.show('Name and Image are required', {
        theme: 'error',
        position: 'bottom-start',
        duration: 5000
      });
      return;
    }

    const api = new RefDataContainersApi(dorcApiConfiguration);
    const isUpdate = !!container.Id;
    if (isUpdate && !(await confirmSharedResourceChange(container, 'edit')))
      return;
    const request = isUpdate
      ? api.refDataContainersIdPut({
          id: container.Id!,
          containerApiModel: container
        })
      : api.refDataContainersPost({ containerApiModel: container });

    request.subscribe({
      next: (saved: ContainerApiModel) => {
        if (!isUpdate && envId > 0 && saved?.Id) {
          this.attachToEnvironment(api, saved, envId);
          return;
        }
        Notification.show(
          `Container ${container.Name} ${isUpdate ? 'updated' : 'created'}`,
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
    saved: ContainerApiModel,
    envId: number
  ) {
    api
      .refDataContainersIdEnvironmentsEnvIdPut({
        id: saved.Id!,
        envId
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
