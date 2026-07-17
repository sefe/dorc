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
import { ContainerApiModel, RefDataContainersApi } from '../../apis/dorc-api';
import '../add-edit-container';
import '../attach-container';
import { PageEnvBase } from './page-env-base';

@customElement('env-containers')
export class EnvContainers extends PageEnvBase {
  @property({ type: Boolean }) private envReadOnly = false;

  @property({ type: Array }) private containers: ContainerApiModel[] = [];

  @state() private attachDialogOpened = false;

  @state() private editDialogOpened = false;

  @state() private editing: ContainerApiModel = {};

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
    this.addEventListener('container-attached', this.onMutated as EventListener);
    this.addEventListener('container-saved', this.onSaved as EventListener);
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Container Details"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; margin: 0px;"
      >
        <div class="details-content">
          <div>
            <vaadin-button
              title="Attach Container"
              .disabled="${this.envReadOnly}"
              @click="${() => (this.attachDialogOpened = true)}"
              >Attach Container</vaadin-button
            >
            <vaadin-button
              title="New Container"
              .disabled="${this.envReadOnly}"
              @click="${this.openCreateDialog}"
              >New Container</vaadin-button
            >
          </div>
          <vaadin-grid id="containers-grid" .items="${this.containers}" theme="compact row-stripes no-row-borders">
            <vaadin-grid-sort-column path="Name" header="Name"></vaadin-grid-sort-column>
            <vaadin-grid-sort-column path="Image" header="Image"></vaadin-grid-sort-column>
            <vaadin-grid-sort-column path="Registry" header="Registry"></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              path="HostServerName"
              header="Host Server"
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
        header-title="Attach Container"
        draggable
        .opened="${this.attachDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.attachDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(
          () => html`<attach-container .envId="${this.environmentId}"></attach-container>`,
          [this.environmentId]
        )}
      ></vaadin-dialog>

      <vaadin-dialog
        header-title="${this.editing.Id ? 'Edit Container' : 'New Container'}"
        draggable
        .opened="${this.editDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.editDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(
          () => html`<add-edit-container .container="${this.editing}"></add-edit-container>`,
          [this.editing]
        )}
      ></vaadin-dialog>
    `;
  }

  private actionsRenderer = (
    root: HTMLElement,
    _column: unknown,
    model: { item: ContainerApiModel }
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
    this.loadContainers();
  }

  private loadContainers() {
    if (this.environmentId <= 0) return;
    new RefDataContainersApi()
      .refDataContainersByEnvIdEnvIdGet({ envId: this.environmentId })
      .subscribe({
        next: (data: ContainerApiModel[]) => {
          this.containers = data;
        },
        error: (err: any) => console.error(err)
      });
  }

  private openCreateDialog() {
    this.editing = {};
    this.editDialogOpened = true;
  }

  private openEditDialog(item: ContainerApiModel) {
    this.editing = item;
    this.editDialogOpened = true;
  }

  private detach(item: ContainerApiModel) {
    if (!item.Id) return;
    new RefDataContainersApi()
      .refDataContainersIdEnvironmentsEnvIdDelete({
        id: item.Id,
        envId: this.environmentId
      })
      .subscribe({
        next: () => {
          Notification.show(`Container ${item.Name} detached`, {
            theme: 'success',
            position: 'bottom-start',
            duration: 5000
          });
          this.loadContainers();
        },
        error: (err: any) => {
          Notification.show(
            `Failed to detach container: ${err.response ?? err.message ?? err}`,
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
    this.loadContainers();
  }

  private onSaved() {
    this.editDialogOpened = false;
    this.loadContainers();
  }
}
