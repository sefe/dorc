import { css, PropertyValues } from 'lit';
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
import { dialogRenderer } from '@vaadin/dialog/lit';
import { Notification } from '@vaadin/notification';
import {
  ApiRegistrationApiModel,
  RefDataApiRegistrationsApi
} from '../../apis/dorc-api';
import '../add-edit-api-registration';
import '../attach-api-registration';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';
import { navigate } from '../../router/router';
import { PageEnvBase } from './page-env-base';

@customElement('env-apis')
export class EnvApis extends PageEnvBase {
  @property({ type: Boolean }) private envReadOnly = false;

  @property({ type: Array })
  private apiRegistrations: ApiRegistrationApiModel[] = [];

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

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);
    this.addEventListener(
      'api-registration-attached',
      this.onMutated as EventListener
    );
    this.addEventListener(
      'api-registration-saved',
      this.onSaved as EventListener
    );
  }

  render() {
    return html`
      <vaadin-details
        opened
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; padding-right: 4px; margin: 0px; box-sizing: border-box;"
      >
        <vaadin-details-summary slot="summary">
          <div class="summary-bar">
            <vaadin-icon icon="vaadin:connect-o" theme="small"></vaadin-icon>
            API Details
            <span class="count-badge">${this.apiRegistrations.length}</span>
          </div>
        </vaadin-details-summary>
        <div class="details-content">
          <div class="toolbar">
            <vaadin-button
              title="Attach API"
              theme="primary"
              .disabled="${this.envReadOnly}"
              @click="${() => (this.attachDialogOpened = true)}"
            >
              <vaadin-icon icon="vaadin:link" slot="prefix"></vaadin-icon>
              Attach API</vaadin-button
            >
            <vaadin-button
              title="New API"
              .disabled="${this.envReadOnly}"
              @click="${this.openCreateDialog}"
            >
              <vaadin-icon icon="vaadin:plus" slot="prefix"></vaadin-icon>
              New API</vaadin-button
            >
          </div>
          <div
            class="empty-state"
            ?hidden="${this.apiRegistrations.length !== 0}"
          >
            <vaadin-icon icon="vaadin:connect-o"></vaadin-icon>
            <span>No APIs attached to this environment yet</span>
          </div>
          <vaadin-grid
            id="api-registrations-grid"
            ?hidden="${this.apiRegistrations.length === 0}"
            .items="${this.apiRegistrations}"
            theme="compact row-stripes no-row-borders"
          >
            <vaadin-grid-sort-column
              path="Name"
              header="Name"
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              path="BaseUrl"
              header="Base URL"
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              path="Version"
              header="Version"
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              path="HealthCheckUrl"
              header="Health Check URL"
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              path="Tags"
              header="Tags"
            ></vaadin-grid-sort-column>
            <vaadin-grid-column
              ${columnBodyRenderer(this.actionsRenderer, [this.envReadOnly])}
              flex-grow="0"
              width="220px"
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
        ${dialogRenderer(this.attachDialogRenderer, [this.environmentId])}
      ></vaadin-dialog>

      <vaadin-dialog
        header-title="${this.editing.Id ? 'Edit API Registration' : 'New API Registration'}"
        draggable
        .opened="${this.editDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.editDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.editDialogRenderer, [this.editing])}
      ></vaadin-dialog>
    `;
  }

  private attachDialogRenderer = () => html`
    <attach-api-registration
      .envId="${this.environmentId}"
    ></attach-api-registration>
  `;

  private editDialogRenderer = () => html`
    <add-edit-api-registration
      .apiRegistration="${this.editing}"
    ></add-edit-api-registration>
  `;

  private actionsRenderer = (item: ApiRegistrationApiModel) => html`
    <vaadin-button
      class="row-button"
      aria-label="View audit history"
      theme="icon"
      @click="${() => this.openAudit(item)}"
    >
      <vaadin-tooltip slot="tooltip" text="View audit history"></vaadin-tooltip>
      <vaadin-icon
        icon="vaadin:calendar-user"
        style="color: var(--dorc-link-color)"
      ></vaadin-icon>
    </vaadin-button>
    <vaadin-button
      class="row-button"
      ?disabled="${this.envReadOnly}"
      @click="${() => this.openEditDialog(item)}"
      >Edit</vaadin-button
    >
    <vaadin-button
      class="row-button"
      ?disabled="${this.envReadOnly}"
      @click="${() => this.detach(item)}"
      >Detach</vaadin-button
    >
  `;

  private openAudit(item: ApiRegistrationApiModel) {
    const id = item.Id ?? 0;
    if (id <= 0) return;
    void navigate(`/api-registrations/audit?apiRegistrationId=${id}`);
  }

  override notifyEnvironmentReady() {
    this.envReadOnly = !this.environment?.UserEditable;
    // The base class assigns `environment` (which fires this hook) before it assigns
    // `environmentId` on the cold-cache path, so derive the id from the environment.
    this.loadApiRegistrations(
      this.environment?.EnvironmentId ?? this.environmentId
    );
  }

  private loadApiRegistrations(envId: number = this.environmentId) {
    if (envId <= 0) return;
    new RefDataApiRegistrationsApi(dorcApiConfiguration)
      .refDataApiRegistrationsByEnvIdEnvIdGet({ envId })
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
    new RefDataApiRegistrationsApi(dorcApiConfiguration)
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
