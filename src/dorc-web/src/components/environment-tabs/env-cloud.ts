import { css, PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/details';
import '@vaadin/dialog';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogRenderer } from '@vaadin/dialog/lit';
import { Notification } from '@vaadin/notification';
import { CloudResourceApiModel, RefDataCloudResourcesApi } from '../../apis/dorc-api';
import '../add-edit-cloud-resource';
import '../attach-cloud-resource';
import { PageEnvBase } from './page-env-base';

@customElement('env-cloud')
export class EnvCloud extends PageEnvBase {
  @property({ type: Boolean }) private envReadOnly = false;

  @property({ type: Array }) private cloudResources: CloudResourceApiModel[] = [];

  @state() private attachDialogOpened = false;

  @state() private editDialogOpened = false;

  @state() private editing: CloudResourceApiModel = {};

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
    `;
  }

  constructor() {
    super();
    super.loadEnvironmentInfo();
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);
    this.addEventListener('cloud-resource-attached', this.onMutated as EventListener);
    this.addEventListener('cloud-resource-saved', this.onSaved as EventListener);
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Cloud Resource Details"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; margin: 0px;"
      >
        <div class="details-content">
          <div>
            <vaadin-button
              title="Attach Cloud Resource"
              .disabled="${this.envReadOnly}"
              @click="${() => (this.attachDialogOpened = true)}"
              >Attach Cloud Resource</vaadin-button
            >
            <vaadin-button
              title="New Cloud Resource"
              .disabled="${this.envReadOnly}"
              @click="${this.openCreateDialog}"
              >New Cloud Resource</vaadin-button
            >
          </div>
          <vaadin-grid id="cloud-resources-grid" .items="${this.cloudResources}" theme="compact row-stripes no-row-borders">
            <vaadin-grid-sort-column path="Name" header="Name"></vaadin-grid-sort-column>
            <vaadin-grid-sort-column path="Provider" header="Provider"></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              path="ResourceType"
              header="Resource Type"
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              path="ResourceIdentifier"
              header="Resource Identifier"
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              path="Subscription"
              header="Subscription"
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column path="Tags" header="Tags"></vaadin-grid-sort-column>
            <vaadin-grid-column
              .renderer="${this.actionsRenderer}"
              flex-grow="0"
              width="180px"
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
        ${dialogRenderer(
          () => html`<attach-cloud-resource .envId="${this.environmentId}"></attach-cloud-resource>`,
          [this.environmentId]
        )}
      ></vaadin-dialog>

      <vaadin-dialog
        header-title="${this.editing.Id ? 'Edit Cloud Resource' : 'New Cloud Resource'}"
        draggable
        .opened="${this.editDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.editDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(
          () => html`<add-edit-cloud-resource .cloudResource="${this.editing}"></add-edit-cloud-resource>`,
          [this.editing]
        )}
      ></vaadin-dialog>
    `;
  }

  private actionsRenderer = (
    root: HTMLElement,
    _column: unknown,
    model: { item: CloudResourceApiModel }
  ) => {
    root.innerHTML = '';
    const edit = document.createElement('vaadin-button');
    edit.className = 'row-button';
    edit.textContent = 'Edit';
    edit.disabled = this.envReadOnly;
    edit.addEventListener('click', () => this.openEditDialog(model.item));
    const detach = document.createElement('vaadin-button');
    detach.className = 'row-button';
    detach.textContent = 'Detach';
    detach.disabled = this.envReadOnly;
    detach.addEventListener('click', () => this.detach(model.item));
    root.append(edit, detach);
  };

  override notifyEnvironmentReady() {
    this.envReadOnly = !this.environment?.UserEditable;
    // The base class assigns `environment` (which fires this hook) before it assigns
    // `environmentId` on the cold-cache path, so derive the id from the environment.
    this.loadCloudResources(this.environment?.EnvironmentId ?? this.environmentId);
  }

  private loadCloudResources(envId: number = this.environmentId) {
    if (envId <= 0) return;
    new RefDataCloudResourcesApi()
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
    this.editing = item;
    this.editDialogOpened = true;
  }

  private detach(item: CloudResourceApiModel) {
    if (!item.Id) return;
    new RefDataCloudResourcesApi()
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
