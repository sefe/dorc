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
import { ApiRegistrationApiModel, RefDataApiRegistrationsApi } from '../../apis/dorc-api';
import '../add-edit-api-registration';
import '../attach-api-registration';
import { PageEnvBase } from './page-env-base';

@customElement('env-apis')
export class EnvApis extends PageEnvBase {
  @property({ type: Boolean }) private envReadOnly = false;

  @property({ type: Array }) private apiRegistrations: ApiRegistrationApiModel[] = [];

  @state() private attachDialogOpened = false;

  @state() private editDialogOpened = false;

  @state() private editing: ApiRegistrationApiModel = {};

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
    this.addEventListener('api-registration-attached', this.onMutated as EventListener);
    this.addEventListener('api-registration-saved', this.onSaved as EventListener);
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="API Details"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; margin: 0px;"
      >
        <div class="details-content">
          <div>
            <vaadin-button
              title="Attach API"
              .disabled="${this.envReadOnly}"
              @click="${() => (this.attachDialogOpened = true)}"
              >Attach API</vaadin-button
            >
            <vaadin-button
              title="New API"
              .disabled="${this.envReadOnly}"
              @click="${this.openCreateDialog}"
              >New API</vaadin-button
            >
          </div>
          <vaadin-grid id="api-registrations-grid" .items="${this.apiRegistrations}" theme="compact row-stripes no-row-borders">
            <vaadin-grid-sort-column path="Name" header="Name"></vaadin-grid-sort-column>
            <vaadin-grid-sort-column path="BaseUrl" header="Base URL"></vaadin-grid-sort-column>
            <vaadin-grid-sort-column path="Version" header="Version"></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              path="HealthCheckUrl"
              header="Health Check URL"
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
        header-title="Attach API"
        draggable
        .opened="${this.attachDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.attachDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(
          () => html`<attach-api-registration .envId="${this.environmentId}"></attach-api-registration>`,
          [this.environmentId]
        )}
      ></vaadin-dialog>

      <vaadin-dialog
        header-title="${this.editing.Id ? 'Edit API Registration' : 'New API Registration'}"
        draggable
        .opened="${this.editDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.editDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(
          () => html`<add-edit-api-registration .apiRegistration="${this.editing}"></add-edit-api-registration>`,
          [this.editing]
        )}
      ></vaadin-dialog>
    `;
  }

  private actionsRenderer = (
    root: HTMLElement,
    _column: unknown,
    model: { item: ApiRegistrationApiModel }
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
    this.loadApiRegistrations();
  }

  private loadApiRegistrations() {
    if (this.environmentId <= 0) return;
    new RefDataApiRegistrationsApi()
      .refDataApiRegistrationsByEnvIdEnvIdGet({ envId: this.environmentId })
      .subscribe({
        next: (data: ApiRegistrationApiModel[]) => {
          this.apiRegistrations = data;
        },
        error: (err: any) => console.error(err)
      });
  }

  private openCreateDialog() {
    this.editing = {};
    this.editDialogOpened = true;
  }

  private openEditDialog(item: ApiRegistrationApiModel) {
    this.editing = item;
    this.editDialogOpened = true;
  }

  private detach(item: ApiRegistrationApiModel) {
    if (!item.Id) return;
    new RefDataApiRegistrationsApi()
      .refDataApiRegistrationsIdEnvironmentsEnvIdDelete({
        id: item.Id,
        envId: this.environmentId
      })
      .subscribe({
        next: () => {
          Notification.show(`API registration ${item.Name} detached`, {
            theme: 'success',
            position: 'bottom-start',
            duration: 5000
          });
          this.loadApiRegistrations();
        },
        error: (err: any) => {
          Notification.show(
            `Failed to detach API registration: ${err.response ?? err.message ?? err}`,
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
    this.loadApiRegistrations();
  }

  private onSaved() {
    this.editDialogOpened = false;
    this.loadApiRegistrations();
  }
}
