import { css, PropertyValues } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '../icons/iron-icons.js';
import '@vaadin/vaadin-lumo-styles/icons.js';
import '../components/add-edit-server';
import '@polymer/paper-dialog';
import '@vaadin/text-field';
import { PaperDialogElement } from '@polymer/paper-dialog';
import '../components/add-permission';
import '../components/edit-permission';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { PermissionDto } from '../apis/dorc-api';
import { RefDataPermissionApi } from '../apis/dorc-api';
import { Notification } from '@vaadin/notification';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever.js';

@customElement('page-permissions-list')
export class PagePermissionsList extends PageElement {
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
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 110px);
        --divider-color: rgb(223, 232, 239);
      }
      .overlay {
        width: 100%;
        height: 100%;
        position: fixed;
      }
      .overlay__inner {
        width: 100%;
        height: 100%;
        position: absolute;
      }
      .overlay__content {
        left: 20%;
        position: absolute;
        top: 20%;
        transform: translate(-50%, -50%);
      }
      .spinner {
        width: 75px;
        height: 75px;
        display: inline-block;
        border-width: 2px;
        border-color: rgba(255, 255, 255, 0.05);
        border-top-color: cornflowerblue;
        animation: spin 1s infinite linear;
        border-radius: 100%;
        border-style: solid;
      }
      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }
      paper-dialog.size-position {
        top: 16px;
        overflow: auto;
        padding: 10px;
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
            style="color: cornflowerblue"
          ></vaadin-icon
          >Add SQL Role...
        </vaadin-button>
      </div>
      <paper-dialog
        class="size-position"
        id="add-permission-dialog"
        allow-click-through
        modal
      >
        <add-permission></add-permission>
        <div style="display: flex; justify-content: flex-end">
          <vaadin-button dialog-confirm>Close</vaadin-button>
        </div>
      </paper-dialog>
      <paper-dialog
        class="size-position"
        id="edit-permission-dialog"
        allow-click-through
        modal
      >
        <edit-permission></edit-permission>
        <div style="display: flex; justify-content: flex-end">
          <vaadin-button dialog-confirm>Close</vaadin-button>
        </div>
      </paper-dialog>
      ${this.loading
        ? html`
            <div class="overlay" style="z-index: 2">
              <div class="overlay__inner">
                <div class="overlay__content">
                  <span class="spinner"></span>
                </div>
              </div>
            </div>
          `
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
              ></vaadin-grid-sort-column>
              <vaadin-grid-column
                header="Actions"
                .renderer=${(root: HTMLElement, _column: any, model: any) => {
                  const permission = model.item as PermissionDto;
                  root.innerHTML = `
                    <vaadin-button 
                      class="edit-btn" 
                      theme="icon"
                      title="Edit Permission"
                      style="margin-right: 5px;">
                      <vaadin-icon icon="lumo:edit" style="color: cornflowerblue;"></vaadin-icon>
                    </vaadin-button>
                    <vaadin-button 
                      class="delete-btn" 
                      theme="icon"
                      title="Delete Permission">
                      <vaadin-icon icon="icons:delete" style="color: #FF3131;"></vaadin-icon>
                    </vaadin-button>
                  `;

                  const editBtn = root.querySelector(
                    '.edit-btn'
                  ) as HTMLElement;
                  const deleteBtn = root.querySelector(
                    '.delete-btn'
                  ) as HTMLElement;

                  if (editBtn) {
                    editBtn.onclick = () => this.editPermission(permission);
                  }

                  if (deleteBtn) {
                    deleteBtn.onclick = () => this.deletePermission(permission);
                  }
                }}
              ></vaadin-grid-column>
            </vaadin-grid>
          `} `;
  }

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

  permissionCreated() {
    this.getPermissionsList();

    const dialog = this.shadowRoot?.getElementById(
      'add-permission-dialog'
    ) as PaperDialogElement;
    dialog.close();
  }

  permissionUpdated() {
    this.getPermissionsList();

    const dialog = this.shadowRoot?.getElementById(
      'edit-permission-dialog'
    ) as PaperDialogElement;
    dialog.close();
  }

  editPermission(permission: PermissionDto) {
    const editPermissionComponent = this.shadowRoot?.querySelector(
      'edit-permission'
    ) as any;
    if (editPermissionComponent) {
      editPermissionComponent.setPermission(permission);
    }

    const dialog = this.shadowRoot?.getElementById(
      'edit-permission-dialog'
    ) as PaperDialogElement;
    dialog.open();
  }

  deletePermission(permission: PermissionDto) {
    const confirmDelete = confirm(
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
    const attachEnv = this.shadowRoot?.getElementById(
      'add-permission-dialog'
    ) as PaperDialogElement;
    attachEnv.open();
  }
}
