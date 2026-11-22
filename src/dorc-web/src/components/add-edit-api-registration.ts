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
import { splitTags } from '../helpers/tag-parser';
import { resolveResourceTagTokens } from './resource-tags';

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
      'ApiRegistrationNames_',
      splitTags(this._apiRegistration.Tags)
    );
    return html`
      <div
        class="token-hints"
        role="region"
        aria-label="Exposed API registration tag tokens"
      >
        <span class="token-hints-label">
          Built-in variable token${tokens.length === 1 ? '' : 's'} exposed by
          API registration tags:
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
                >Add a tag (e.g. <code>rest</code>) to expose
                <code>$ApiRegistrationNames_rest$</code> in environment
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
      ${this.renderTokenHints()}
      <vaadin-button theme="primary" @click="${this.save}">
        ${this._apiRegistration.Id ? 'Save' : 'Create'}
      </vaadin-button>
    `;
  }

  private setField(field: keyof ApiRegistrationApiModel, e: Event) {
    const value = (e.currentTarget as HTMLInputElement).value;
    this._apiRegistration = { ...this._apiRegistration, [field]: value };
    this.requestUpdate();
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
