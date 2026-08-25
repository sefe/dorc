import { columnBodyRenderer } from '@vaadin/grid/lit';
import { confirmPrompt } from '../components/confirm-prompt';
import { css, nothing, PropertyValues } from 'lit';
import '../components/dorc-spinner';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '../icons/iron-icons.js';
import '@vaadin/vaadin-lumo-styles/icons.js';
import '../components/add-edit-server';
import '@vaadin/dialog';
import '@vaadin/text-field';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { ref } from 'lit/directives/ref.js';
import '../components/add-permission';
import '../components/edit-permission';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import { PermissionDto } from '../apis/dorc-api';
import { RefDataPermissionApi } from '../apis/dorc-api';
import { Notification } from '@vaadin/notification';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever.js';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/tooltip';
import { UnsavedChangesGuard } from '../components/unsaved-changes-guard';

@customElement('page-permissions-list')
export class PagePermissionsList extends ResponsiveMixin(PageElement) {
  private readonly unsavedChanges = new UnsavedChangesGuard();

  @state() addPermissionDialogOpened = false;

  @state() editPermissionDialogOpened = false;

  @state() editingPermission: PermissionDto | null = null;

  @property({ type: Array }) permissions: Array<PermissionDto> = [];

  @property({ type: Array }) filteredPermissions: Array<PermissionDto> = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  private loading = true;

  constructor() {
    super();
    this.getPermissionsList();
  }

  private getPermissionsList() {
    const api = new RefDataPermissionApi();
    api.refDataPermissionGet().subscribe(
      (data: PermissionDto[]) => {
        this.setPermissions(data);
      },

      (err: any) => console.error(err),
      () => console.log('done loading permissions')
    );
  }

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;
        --divider-color: var(--dorc-border-color);
      }
      vaadin-grid#grid {
        flex: 1;
        min-height: 0;
      }
      vaadin-dialog::part(overlay) {
        top: 16px;
        overflow: auto;
        max-width: calc(100vw - 32px);
      }
      @media (max-width: 768px) {
        vaadin-grid-cell-content {
          white-space: normal;
          word-wrap: break-word;
          overflow-wrap: break-word;
        }
      }
    `;
  }

  render() {
    return html`<div style="display: inline">
        <vaadin-text-field
          style="padding-left: 5px; width: 50%;"
          placeholder="Search"
          @value-changed="${this.updateSearch}"
          clear-button-visible
          helper-text="Use | for multiple search terms"
        >
          <vaadin-icon slot="prefix" icon="vaadin:search"></vaadin-icon>
        </vaadin-text-field>
        <vaadin-button
          title="Add SQL Role"
          style="width: 250px"
          @click="${this.addPermission}"
        >
          <vaadin-icon
            icon="vaadin:key"
            style="color: var(--dorc-link-color)"
          ></vaadin-icon
          >Add SQL Role...
        </vaadin-button>
      </div>
      <vaadin-dialog
        ${ref(this.unsavedChanges.attach)}
        id="add-permission-dialog"
        header-title="Add SQL Role"
        draggable
        .opened="${this.addPermissionDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.addPermissionDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderAddPermission, [])}
        ${dialogFooterRenderer(this.renderAddPermissionFooter, [])}
      ></vaadin-dialog>
      <vaadin-dialog
        ${ref(this.unsavedChanges.attach)}
        id="edit-permission-dialog"
        header-title="Edit SQL Role"
        draggable
        .opened="${this.editPermissionDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.editPermissionDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderEditPermission, [this.editingPermission])}
        ${dialogFooterRenderer(this.renderEditPermissionFooter, [])}
      ></vaadin-dialog>
      ${
        this.loading
          ? html` <dorc-spinner></dorc-spinner> `
          : html`
              <vaadin-grid
                id="grid"
                .items=${this.filteredPermissions}
                column-reordering-allowed
                multi-sort
                theme="compact row-stripes no-row-borders no-border"
              >
                <vaadin-grid-sort-column
                  path="DisplayName"
                  header="Display Name"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  path="PermissionName"
                  header="Permission Name"
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-column
                  header="Actions"
                  ${columnBodyRenderer(this.permissionActionsRenderer, [])}
                ></vaadin-grid-column>
              </vaadin-grid>
            `
      } `;
  }

  private permissionActionsRenderer = (permission: PermissionDto) => html`
    <vaadin-button
      theme="icon"
      aria-label="Edit Permission"
      style="margin-right: 5px;"
      @click="${() => this.editPermission(permission)}"
    >
      <vaadin-tooltip slot="tooltip" text="Edit Permission"></vaadin-tooltip>
      <vaadin-icon
        icon="lumo:edit"
        style="color: var(--dorc-link-color);"
      ></vaadin-icon>
    </vaadin-button>
    <vaadin-button
      theme="icon"
      aria-label="Delete Permission"
      @click="${() => this.deletePermission(permission)}"
    >
      <vaadin-tooltip slot="tooltip" text="Delete Permission"></vaadin-tooltip>
      <vaadin-icon
        icon="icons:delete"
        style="color: var(--dorc-error-color);"
      ></vaadin-icon>
    </vaadin-button>
  `;

  updateSearch(e: CustomEvent) {
    const value = (e.detail.value as string) || '';
    const filters = value
      .trim()
      .split('|')
      .map(filter => new RegExp(filter, 'i'));

    this.filteredPermissions = this.permissions.filter(
      ({ DisplayName, PermissionName }) =>
        filters.some(
          filter =>
            filter.test(DisplayName || '') || filter.test(PermissionName || '')
        )
    );
  }

  firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'permission-created',
      this.permissionCreated as EventListener
    );

    this.addEventListener(
      'permission-updated',
      this.permissionUpdated as EventListener
    );
  }

  private renderAddPermission = () => html`<add-permission></add-permission>`;

  private renderAddPermissionFooter = () => html`
    <vaadin-button @click="${() => (this.addPermissionDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  /**
   * The permission is pushed in through `ref`, which fires exactly when the
   * element is created. Previously it was set by querying for `<edit-permission>`
   * *before* opening the dialog; under a renderer the element does not exist
   * until the dialog opens, so that ordering no longer holds.
   */
  private renderEditPermission = () =>
    this.editingPermission
      ? html`<edit-permission
          ${ref(el => {
            if (el && this.editingPermission) {
              (
                el as unknown as {
                  setPermission(p: PermissionDto): void;
                }
              ).setPermission(this.editingPermission);
            }
          })}
        ></edit-permission>`
      : nothing;

  private renderEditPermissionFooter = () => html`
    <vaadin-button @click="${() => (this.editPermissionDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  permissionCreated() {
    this.getPermissionsList();
    this.addPermissionDialogOpened = false;
  }

  permissionUpdated() {
    this.getPermissionsList();
    this.editPermissionDialogOpened = false;
  }

  editPermission(permission: PermissionDto) {
    this.editingPermission = permission;
    this.editPermissionDialogOpened = true;
  }

  async deletePermission(permission: PermissionDto) {
    const confirmDelete = await confirmPrompt(
      `Are you sure you want to delete the role "${permission.DisplayName}"?`
    );

    if (confirmDelete && permission.Id) {
      const api = new RefDataPermissionApi();
      api.refDataPermissionDelete({ id: permission.Id }).subscribe({
        next: () => {
          this.getPermissionsList();
          Notification.show(
            `Permission "${permission.DisplayName}" deleted successfully`,
            {
              theme: 'success',
              position: 'bottom-start',
              duration: 3000
            }
          );
        },
        error: (err: any) => {
          const errMessage = retrieveErrorMessage(err);
          console.error(`Error deleting permission: ${errMessage}`, err);
          Notification.show(`Error deleting permission: ${errMessage}`, {
            theme: 'error',
            position: 'bottom-start',
            duration: 5000
          });
        }
      });
    }
  }

  setPermissions(permissionDtos: PermissionDto[]) {
    this.permissions = permissionDtos;
    this.filteredPermissions = permissionDtos;
    this.loading = false;
  }

  addPermission() {
    this.addPermissionDialogOpened = true;
  }
}
