import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/text-field';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import {
  CloudResourceApiModel,
  RefDataCloudResourcesApi
} from '../apis/dorc-api';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { confirmSharedResourceChange } from '../helpers/shared-resource-confirm';
import { splitTags } from '../helpers/tag-parser';
import { resolveResourceTagTokens } from './resource-tags';

/**
 * Create/edit form for a cloud resource definition. Fires `cloud-resource-saved`.
 * When `envId` is set, a newly created cloud resource is attached to that
 * environment as part of the save.
 */
@customElement('add-edit-cloud-resource')
export class AddEditCloudResource extends LitElement {
  @property({ type: Number }) envId = 0;

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
      'CloudResourceNames_',
      splitTags(this._cloudResource.Tags)
    );
    return html`
      <div
        class="token-hints"
        role="region"
        aria-label="Exposed cloud resource tag tokens"
      >
        <span class="token-hints-label">
          Built-in variable token${tokens.length === 1 ? '' : 's'} exposed by
          cloud resource tags:
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
                >Add a tag (e.g. <code>messaging</code>) to expose
                <code>$CloudResourceNames_messaging$</code> in environment
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
        placeholder="e.g. service-bus-trading-ut2"
        maxlength="${this.maxFieldLength}"
        .value="${this._cloudResource.Name ?? ''}"
        @input="${(e: Event) => this.setField('Name', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Provider"
        required
        placeholder="e.g. Azure"
        helper-text="e.g. Azure, Aiven, AWS"
        maxlength="${this.maxFieldLength}"
        .value="${this._cloudResource.Provider ?? ''}"
        @input="${(e: Event) => this.setField('Provider', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Resource Type"
        required
        placeholder="e.g. azurerm_servicebus_namespace"
        helper-text="Terraform resource type or provider service type"
        maxlength="${this.maxFieldLength}"
        .value="${this._cloudResource.ResourceType ?? ''}"
        @input="${(e: Event) => this.setField('ResourceType', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Resource Identifier"
        required
        placeholder="e.g. /subscriptions/…/resourceGroups/rg-ut2/providers/Microsoft.ServiceBus/namespaces/sb-trading-ut2"
        helper-text="Azure resource ID, or project/service/name for Aiven"
        maxlength="${this.maxIdentifierLength}"
        .value="${this._cloudResource.ResourceIdentifier ?? ''}"
        @input="${(e: Event) => this.setField('ResourceIdentifier', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Subscription"
        placeholder="e.g. 11111111-2222-4333-8444-555555555555"
        helper-text="Subscription ID or name the resource lives in"
        maxlength="${this.maxFieldLength}"
        .value="${this._cloudResource.Subscription ?? ''}"
        @input="${(e: Event) => this.setField('Subscription', e)}"
      ></vaadin-text-field>
      <vaadin-text-field
        label="Tags"
        placeholder="e.g. messaging;trading"
        helper-text="Semicolon-separated"
        maxlength="${this.maxFieldLength}"
        .value="${this._cloudResource.Tags ?? ''}"
        @input="${(e: Event) => this.setField('Tags', e)}"
      ></vaadin-text-field>
      ${this.renderTokenHints()}
      <vaadin-button theme="primary" @click="${this.save}">
        ${this._cloudResource.Id ? 'Save' : 'Create'}
      </vaadin-button>
    `;
  }

  private setField(field: keyof CloudResourceApiModel, e: Event) {
    const value = (e.currentTarget as HTMLInputElement).value;
    this._cloudResource = { ...this._cloudResource, [field]: value };
    this.requestUpdate();
  }

  private async save() {
    const cloudResource = { ...this._cloudResource };
    const envId = this.envId;
    if (
      !cloudResource.Name ||
      !cloudResource.Provider ||
      !cloudResource.ResourceType ||
      !cloudResource.ResourceIdentifier
    ) {
      Notification.show(
        'Name, Provider, Resource Type and Resource Identifier are required',
        {
          theme: 'error',
          position: 'bottom-start',
          duration: 5000
        }
      );
      return;
    }

    const api = new RefDataCloudResourcesApi(dorcApiConfiguration);
    const isUpdate = !!cloudResource.Id;
    if (isUpdate && !(await confirmSharedResourceChange(cloudResource, 'edit')))
      return;
    const request = isUpdate
      ? api.refDataCloudResourcesIdPut({
          id: cloudResource.Id!,
          cloudResourceApiModel: cloudResource
        })
      : api.refDataCloudResourcesPost({
          cloudResourceApiModel: cloudResource
        });

    request.subscribe({
      next: (saved: CloudResourceApiModel) => {
        if (!isUpdate && envId > 0 && saved?.Id) {
          this.attachToEnvironment(api, saved, envId);
          return;
        }
        Notification.show(
          `Cloud resource ${cloudResource.Name} ${isUpdate ? 'updated' : 'created'}`,
          { theme: 'success', position: 'bottom-start', duration: 5000 }
        );
        this.fireSaved();
      },
      error: (err: any) => {
        Notification.show(
          `Failed to save cloud resource: ${err.response ?? err.message ?? err}`,
          { theme: 'error', position: 'bottom-start', duration: 5000 }
        );
      }
    });
  }

  private attachToEnvironment(
    api: RefDataCloudResourcesApi,
    saved: CloudResourceApiModel,
    envId: number
  ) {
    api
      .refDataCloudResourcesIdEnvironmentsEnvIdPut({
        id: saved.Id!,
        envId
      })
      .subscribe({
        next: () => {
          Notification.show(
            `Cloud resource ${saved.Name} created and attached to this environment`,
            { theme: 'success', position: 'bottom-start', duration: 5000 }
          );
          this.fireSaved();
        },
        error: (err: any) => {
          Notification.show(
            `Cloud resource ${saved.Name} was created but could not be attached: ${err.response ?? err.message ?? err}`,
            { theme: 'error', position: 'bottom-start', duration: 5000 }
          );
          this.fireSaved();
        }
      });
  }

  private fireSaved() {
    this.dispatchEvent(
      new CustomEvent('cloud-resource-saved', { bubbles: true, composed: true })
    );
  }
}
