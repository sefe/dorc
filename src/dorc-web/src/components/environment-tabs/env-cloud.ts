import { css, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/details';
import '@vaadin/dialog';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/tooltip';
import { columnBodyRenderer } from '@vaadin/grid/lit';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { Notification } from '@vaadin/notification';
import {
  CloudResourceApiModel,
  RefDataCloudResourcesApi
} from '../../apis/dorc-api';
import '../add-edit-cloud-resource';
import '../attach-cloud-resource';
import '../resource-tags';
import { joinTags, splitTags } from '../../helpers/tag-parser';
import { map } from 'lit/directives/map.js';
import { keyed } from 'lit/directives/keyed.js';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';
import { navigate } from '../../router/router';
import { PageEnvBase } from './page-env-base';

@customElement('env-cloud')
export class EnvCloud extends PageEnvBase {
  @property({ type: Boolean }) private envReadOnly = false;

  @property({ type: Array }) private cloudResources: CloudResourceApiModel[] =
    [];

  @state() private attachDialogOpened = false;

  @state() private editDialogOpened = false;

  @state() private editing: CloudResourceApiModel = {};

  @state() private tagsDialogOpened = false;

  @state() private tagsItem: CloudResourceApiModel | undefined;

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        width: 100%;
        height: 100%;
      }
      vaadin-details {
        overflow: hidden;
        width: calc(100% - 4px);
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
      }
      vaadin-details::part(content) {
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
        overflow: hidden;
      }
      .details-content {
        display: flex;
        flex-direction: column;
        flex: 1;
        min-height: 0;
      }
      vaadin-grid {
        flex: 1;
        min-height: 0;
      }
      .row-button {
        font-size: var(--lumo-font-size-s);
        color: var(--dorc-link-color);
        padding: var(--lumo-space-xs);
      }
      .tag {
        font-size: var(--lumo-font-size-s);
        font-family: monospace;
        background-color: var(--dorc-chip-bg);
        color: var(--dorc-chip-text);
        display: inline-block;
        padding: 3px;
        margin: 3px;
        text-decoration: none;
        border-radius: 3px;
      }
      .summary-bar {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-s);
        font-weight: 400;
      }
      .summary-bar vaadin-icon {
        color: var(--dorc-link-color);
      }
      .count-badge {
        background-color: var(--dorc-link-color);
        color: var(--dorc-bg-primary);
        border-radius: 1em;
        font-size: var(--lumo-font-size-xs);
        font-weight: 600;
        padding: 0 var(--lumo-space-s);
        line-height: 1.6;
      }
      .toolbar {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-s);
        padding: var(--lumo-space-s) 0;
      }
      .empty-state[hidden],
      vaadin-grid[hidden] {
        display: none;
      }
      .empty-state {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        flex: 1;
        min-height: 120px;
        color: var(--dorc-text-secondary);
        gap: var(--lumo-space-s);
      }
      .empty-state vaadin-icon {
        width: 40px;
        height: 40px;
        opacity: 0.5;
      }
    `;
  }

  constructor() {
    super();
    super.loadEnvironmentInfo();
  }

  render() {
    return html`
      <vaadin-details
        opened
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; padding-right: 4px; margin: 0px; box-sizing: border-box;"
      >
        <vaadin-details-summary slot="summary">
          <div class="summary-bar">
            <vaadin-icon icon="vaadin:cloud" theme="small"></vaadin-icon>
            Cloud Resource Details
            <span class="count-badge">${this.cloudResources.length}</span>
          </div>
        </vaadin-details-summary>
        <div class="details-content">
          <div class="toolbar">
            <vaadin-button
              title="Attach Cloud Resource"
              .disabled="${this.envReadOnly}"
              @click="${() => (this.attachDialogOpened = true)}"
            >
              <vaadin-icon icon="vaadin:link" slot="prefix"></vaadin-icon>
              Attach Cloud Resource</vaadin-button
            >
            <vaadin-button
              title="New Cloud Resource"
              .disabled="${this.envReadOnly}"
              @click="${this.openCreateDialog}"
            >
              <vaadin-icon icon="vaadin:plus" slot="prefix"></vaadin-icon>
              New Cloud Resource</vaadin-button
            >
          </div>
          <div
            class="empty-state"
            ?hidden="${this.cloudResources.length !== 0}"
          >
            <vaadin-icon icon="vaadin:cloud"></vaadin-icon>
            <span>No cloud resources attached to this environment yet</span>
          </div>
          <vaadin-grid
            id="cloud-resources-grid"
            ?hidden="${this.cloudResources.length === 0}"
            .items="${this.cloudResources}"
            theme="compact row-stripes no-row-borders no-border"
          >
            <vaadin-grid-sort-column
              resizable
              path="Name"
              header="Name"
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              resizable
              path="Provider"
              header="Provider"
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              resizable
              path="ResourceType"
              header="Resource Type"
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              resizable
              path="ResourceIdentifier"
              header="Resource Identifier"
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              resizable
              path="Subscription"
              header="Subscription"
            ></vaadin-grid-sort-column>
            <vaadin-grid-column
              ${columnBodyRenderer(this.tagsRenderer, [])}
              resizable
              header="Tags"
            ></vaadin-grid-column>
            <vaadin-grid-column
              ${columnBodyRenderer(this.actionsRenderer, [this.envReadOnly])}
              flex-grow="0"
              width="220px"
            ></vaadin-grid-column>
          </vaadin-grid>
        </div>
      </vaadin-details>

      <vaadin-dialog
        header-title="Attach Cloud Resource"
        draggable
        .opened="${this.attachDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.attachDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.attachDialogRenderer, [this.environmentId])}
      ></vaadin-dialog>

      <vaadin-dialog
        header-title="${this.editing.Id ? 'Edit Cloud Resource' : 'New Cloud Resource'}"
        draggable
        .opened="${this.editDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.editDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.editDialogRenderer, [this.editing, this.environmentId])}
      ></vaadin-dialog>

      <vaadin-dialog
        header-title="Edit Cloud Resource Tags for ${this.tagsItem?.Name ?? ''}"
        draggable
        .opened="${this.tagsDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.tagsDialogOpened = e.detail.value;
          if (!this.tagsDialogOpened) this.tagsItem = undefined;
        }}"
        ${dialogRenderer(this.tagsDialogRenderer, [this.tagsItem])}
        ${dialogFooterRenderer(this.tagsDialogFooterRenderer, [])}
      ></vaadin-dialog>
    `;
  }

  private attachDialogRenderer = () => html`
    <attach-cloud-resource
      .envId="${this.environmentId}"
      @cloud-resource-attached="${this.onMutated}"
    ></attach-cloud-resource>
  `;

  private editDialogRenderer = () =>
    html`${keyed(
      this.editing,
      html`
        <add-edit-cloud-resource
          .cloudResource="${this.editing}"
          .envId="${this.environmentId}"
          @cloud-resource-saved="${this.onSaved}"
        ></add-edit-cloud-resource>
      `
    )}`;

  private tagsDialogRenderer = () =>
    this.tagsItem
      ? html`<resource-tags
          .tags="${this.tagsItem.Tags}"
          @resource-tags-save="${this.saveTags}"
        ></resource-tags>`
      : nothing;

  private tagsDialogFooterRenderer = () => html`
    <vaadin-button @click="${() => (this.tagsDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  private tagsRenderer = (item: CloudResourceApiModel) => html`
    ${map(
      splitTags(item.Tags),
      value => html`<button style="border: 0px" class="tag">${value}</button>`
    )}
  `;

  private actionsRenderer = (item: CloudResourceApiModel) => {
    const editColor = this.envReadOnly
      ? 'var(--dorc-text-secondary)'
      : 'var(--dorc-link-color)';
    const detachColor = this.envReadOnly
      ? 'var(--dorc-text-secondary)'
      : 'var(--dorc-error-color)';
    return html`
      <vaadin-button
        class="row-button"
        aria-label="View audit history"
        theme="icon"
        @click="${() => this.openAudit(item)}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="View audit history"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="vaadin:calendar-user"
          style="color: var(--dorc-link-color)"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        class="row-button"
        aria-label="Edit cloud resource"
        theme="icon"
        ?disabled="${this.envReadOnly}"
        @click="${() => this.openEditDialog(item)}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="Edit cloud resource"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="vaadin:edit"
          style="color: ${editColor}"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        class="row-button"
        aria-label="Edit Cloud Resource Tags"
        theme="icon"
        ?disabled="${this.envReadOnly}"
        @click="${() => this.openTagsDialog(item)}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="Edit Cloud Resource Tags"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="vaadin:tags"
          style="color: ${editColor}"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        class="row-button"
        aria-label="Detach cloud resource"
        theme="icon"
        ?disabled="${this.envReadOnly}"
        @click="${() => this.detach(item)}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="Detach cloud resource"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="vaadin:unlink"
          style="color: ${detachColor}"
        ></vaadin-icon>
      </vaadin-button>
    `;
  };

  private openAudit(item: CloudResourceApiModel) {
    const id = item.Id ?? 0;
    if (id <= 0) return;
    void navigate(`/cloud-resources/audit?cloudResourceId=${id}`);
  }

  override notifyEnvironmentReady() {
    this.envReadOnly = !this.environment?.UserEditable;
    // The base class assigns `environment` (which fires this hook) before it assigns
    // `environmentId` on the cold-cache path, so derive the id from the environment.
    this.loadCloudResources(
      this.environment?.EnvironmentId ?? this.environmentId
    );
  }

  private loadCloudResources(envId: number = this.environmentId) {
    if (envId <= 0) return;
    new RefDataCloudResourcesApi(dorcApiConfiguration)
      .refDataCloudResourcesByEnvIdEnvIdGet({ envId })
      .subscribe({
        next: (data: CloudResourceApiModel[]) => {
          this.cloudResources = data;
        },
        error: (err: any) => console.error(err)
      });
  }

  private openCreateDialog() {
    this.editing = {};
    this.editDialogOpened = true;
  }

  private openEditDialog(item: CloudResourceApiModel) {
    this.editing = { ...item };
    this.editDialogOpened = true;
  }

  private openTagsDialog(item: CloudResourceApiModel) {
    this.tagsItem = item;
    this.tagsDialogOpened = true;
  }

  private saveTags(e: CustomEvent) {
    const item = this.tagsItem;
    if (!item?.Id) return;
    new RefDataCloudResourcesApi(dorcApiConfiguration)
      .refDataCloudResourcesIdPut({
        id: item.Id,
        cloudResourceApiModel: { ...item, Tags: joinTags(e.detail.tags) }
      })
      .subscribe({
        next: () => {
          Notification.show(`Updated tags for cloud resource ${item.Name}`, {
            theme: 'success',
            position: 'bottom-start',
            duration: 5000
          });
          this.tagsDialogOpened = false;
          this.loadCloudResources();
        },
        error: (err: any) => {
          Notification.show(
            `Failed to update cloud resource tags: ${err.response ?? err.message ?? err}`,
            { theme: 'error', position: 'bottom-start', duration: 5000 }
          );
        }
      });
  }

  private detach(item: CloudResourceApiModel) {
    if (!item.Id) return;
    new RefDataCloudResourcesApi(dorcApiConfiguration)
      .refDataCloudResourcesIdEnvironmentsEnvIdDelete({
        id: item.Id,
        envId: this.environmentId
      })
      .subscribe({
        next: () => {
          Notification.show(`Cloud resource ${item.Name} detached`, {
            theme: 'success',
            position: 'bottom-start',
            duration: 5000
          });
          this.loadCloudResources();
        },
        error: (err: any) => {
          Notification.show(
            `Failed to detach cloud resource: ${err.response ?? err.message ?? err}`,
            { theme: 'error', position: 'bottom-start', duration: 5000 }
          );
        }
      });
  }

  private onMutated(e: CustomEvent) {
    this.attachDialogOpened = false;
    if (e.detail?.message) {
      Notification.show(e.detail.message, {
        theme: 'success',
        position: 'bottom-start',
        duration: 5000
      });
    }
    this.loadCloudResources();
  }

  private onSaved() {
    this.editDialogOpened = false;
    this.loadCloudResources();
  }
}
