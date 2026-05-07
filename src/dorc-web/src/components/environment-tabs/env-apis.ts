import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/dialog';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icon';
import { css, render } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Notification } from '@vaadin/notification';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { GridItemModel } from '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { PageEnvBase } from './page-env-base';
import {
  ApiApiModel,
  ApiBoolResult,
  EnvironmentContentApiModel,
  RefDataApisApi,
  RefDataEnvironmentsDetailsApi
} from '../../apis/dorc-api';
import { ApiEndpointResolutionStatus } from '../../apis/dorc-api/models/ApiApiModel';
import '../add-edit-api';
import { AddEditApi } from '../add-edit-api';

@customElement('env-apis')
export class EnvApis extends PageEnvBase {
  @state() apis: ApiApiModel[] = [];

  @state() private envReadOnly = true;

  @state() private editDialogOpened = false;

  @state() private editingApi: ApiApiModel | null = null;

  constructor() {
    super();
    super.loadEnvironmentInfo();
  }

  static get styles() {
    return css`
      :host {
        width: 100%;
      }
      vaadin-details {
        overflow: auto;
        width: calc(100% - 4px);
        height: calc(100vh - 180px);
        --divider-color: var(--dorc-border-color);
      }
      .toolbar {
        margin: 4px 0 8px 0;
      }
      vaadin-grid#grid {
        overflow: hidden;
        width: calc(100% - 4px);
        --divider-color: var(--dorc-border-color);
      }
      .resolved-link {
        color: var(--dorc-link-color);
        text-decoration: none;
      }
      .resolved-link:hover {
        text-decoration: underline;
      }
      .unresolved {
        color: var(--dorc-error-color);
        font-style: italic;
      }
      .badge {
        display: inline-block;
        padding: 1px 6px;
        margin-left: 6px;
        border-radius: 8px;
        background-color: var(--dorc-chip-bg);
        color: var(--dorc-chip-text);
        font-size: 11px;
      }
      .badge.warn {
        background-color: var(--dorc-error-color);
        color: white;
      }
      .row-buttons {
        display: flex;
        gap: 4px;
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Environment APIs"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; margin: 0px;"
      >
        <div class="toolbar">
          <vaadin-button
            title="Add API"
            theme="primary"
            .disabled="${this.envReadOnly}"
            @click="${this.openAddDialog}"
            >Add API</vaadin-button
          >
        </div>

        <vaadin-grid
          id="grid"
          .items="${this.apis}"
          theme="compact row-stripes no-row-borders no-border"
          all-rows-visible
        >
          <vaadin-grid-sort-column
            path="Name"
            header="Name"
            resizable
          ></vaadin-grid-sort-column>
          <vaadin-grid-sort-column
            path="Type"
            header="Type"
            width="80px"
            flex-grow="0"
            resizable
          ></vaadin-grid-sort-column>
          <vaadin-grid-sort-column
            path="Endpoint"
            header="Endpoint (raw)"
            resizable
          ></vaadin-grid-sort-column>
          <vaadin-grid-column
            header="Endpoint (resolved)"
            resizable
            .renderer="${this.resolvedRenderer}"
          ></vaadin-grid-column>
          <vaadin-grid-sort-column
            path="AuthType"
            header="Auth"
            width="90px"
            flex-grow="0"
            resizable
          ></vaadin-grid-sort-column>
          <vaadin-grid-sort-column
            path="OwnerProjectName"
            header="Owner Project"
            resizable
          ></vaadin-grid-sort-column>
          <vaadin-grid-sort-column
            path="Tags"
            header="Tags"
            resizable
          ></vaadin-grid-sort-column>
          <vaadin-grid-column
            header=""
            width="120px"
            flex-grow="0"
            .renderer="${this.actionsRenderer}"
          ></vaadin-grid-column>
        </vaadin-grid>

        <vaadin-dialog
          id="edit-api-dialog"
          .headerTitle="${(this.editingApi?.Id ?? 0) > 0
            ? 'Edit API'
            : 'Add API'}"
          .opened="${this.editDialogOpened}"
          draggable
          @opened-changed="${(event: DialogOpenedChangedEvent) => {
            this.editDialogOpened = event.detail.value;
            if (!event.detail.value) {
              this.editingApi = null;
            }
          }}"
          ${dialogRenderer(this.renderEditDialog, [this.editingApi, this.apis])}
          ${dialogFooterRenderer(this.renderEditFooter, [])}
        ></vaadin-dialog>
      </vaadin-details>
    `;
  }

  override notifyEnvironmentContentReady() {
    this.envReadOnly = !this.environment?.UserEditable;
    this.applyEnvContentApis(this.envContent);
  }

  private applyEnvContentApis(content: EnvironmentContentApiModel | undefined) {
    this.apis = (content?.Apis ?? []).slice().sort((a, b) =>
      (a.Name ?? '').localeCompare(b.Name ?? '')
    );
  }

  private refresh() {
    if (!this.environmentId || this.environmentId === -1) return;
    const detailsApi = new RefDataEnvironmentsDetailsApi();
    detailsApi
      .refDataEnvironmentsDetailsIdGet({ id: this.environmentId })
      .subscribe({
        next: (data: EnvironmentContentApiModel) =>
          this.applyEnvContentApis(data),
        error: err => console.error(err)
      });
  }

  private openAddDialog() {
    this.editingApi = null;
    this.editDialogOpened = true;
  }

  private openEditDialog(api: ApiApiModel) {
    this.editingApi = api;
    this.editDialogOpened = true;
  }

  private closeDialog() {
    this.editDialogOpened = false;
    this.editingApi = null;
  }

  private deleteApi(api: ApiApiModel) {
    const id = api.Id ?? 0;
    if (id <= 0) return;
    if (!confirm(`Delete API '${api.Name}'?`)) return;

    const apisApi = new RefDataApisApi();
    apisApi.refDataApisDelete({ id }).subscribe({
      next: (result: ApiBoolResult) => {
        if (result?.Result) {
          Notification.show('API deleted', {
            theme: 'success',
            position: 'bottom-start',
            duration: 3000
          });
          this.refresh();
        } else {
          Notification.show('Delete failed', {
            theme: 'error',
            position: 'bottom-start',
            duration: 5000
          });
        }
      },
      error: err => {
        console.error(err);
        Notification.show(this.formatError(err, 'Delete failed'), {
          theme: 'error',
          position: 'bottom-start',
          duration: 5000
        });
      }
    });
  }

  private formatError(err: unknown, fallback: string): string {
    const e = err as { response?: unknown; message?: unknown } | null | undefined;
    if (e == null) return fallback;
    if (typeof e.response === 'string' && e.response.length > 0) return e.response;
    if (typeof e.message === 'string' && e.message.length > 0) return e.message;
    if (e.response != null) {
      try {
        return JSON.stringify(e.response);
      } catch {
        // fall through
      }
    }
    return fallback;
  }

  private onApiSaved(eventLabel: string) {
    Notification.show(eventLabel, {
      theme: 'success',
      position: 'bottom-start',
      duration: 3000
    });
    this.closeDialog();
    this.refresh();
  }

  private renderEditDialog = () => html`
    <add-edit-api
      id="add-edit-api"
      .envId="${this.environmentId}"
      .api="${this.editingApi ?? {
        Id: 0,
        Name: '',
        Endpoint: '',
        Type: 'REST',
        AuthType: 'None'
      }}"
      .existingApis="${this.apis}"
      @api-created="${() => this.onApiSaved('API created')}"
      @api-updated="${() => this.onApiSaved('API updated')}"
    ></add-edit-api>
  `;

  private renderEditFooter = () => html`
    <div style="display: flex; justify-content: flex-end">
      <vaadin-button @click="${this.closeDialog}">Close</vaadin-button>
    </div>
  `;

  private resolvedRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<ApiApiModel>
  ) => {
    const api = model.item;
    const resolved = api.EndpointResolved ?? '';
    // Default to NoTokens when the field is absent so a missing/legacy server response
    // never silently falls into the plain-text branch when it should warn.
    const status = api.ResolutionStatus ?? ApiEndpointResolutionStatus.NoTokens;
    const isUrl = /^https?:\/\//i.test(resolved);

    let body;
    if (status === ApiEndpointResolutionStatus.PartiallyResolved) {
      body = html`
        <span class="unresolved" title="Unresolved tokens: ${api.UnresolvedTokens ?? ''}">${resolved}</span>
        <span class="badge warn" title="Missing variables: ${api.UnresolvedTokens ?? ''}">!</span>
      `;
    } else if (isUrl) {
      body = html`
        <a class="resolved-link" href="${resolved}" target="_blank" rel="noopener noreferrer">${resolved}</a>
      `;
    } else {
      body = html`<span>${resolved}</span>`;
    }

    render(body, root);
  };

  private actionsRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<ApiApiModel>
  ) => {
    const api = model.item;
    render(
      html`
        <div class="row-buttons">
          <vaadin-button
            theme="tertiary small"
            title="Edit"
            .disabled="${this.envReadOnly}"
            @click="${() => this.openEditDialog(api)}"
          >
            <vaadin-icon icon="vaadin:edit"></vaadin-icon>
          </vaadin-button>
          <vaadin-button
            theme="tertiary small error"
            title="Delete"
            .disabled="${this.envReadOnly}"
            @click="${() => this.deleteApi(api)}"
          >
            <vaadin-icon icon="vaadin:trash"></vaadin-icon>
          </vaadin-button>
        </div>
      `,
      root
    );
  };
}

// Re-export for type consumers in tests / other modules
export { AddEditApi };
