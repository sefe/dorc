import '@polymer/paper-dialog';
import { PaperDialogElement } from '@polymer/paper-dialog';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/grid/vaadin-grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/text-field';
import { css, PropertyValues, render, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import AppConfig from '../app-config';
import '../components/add-user-or-group/add-user-or-group';
import { Configuration, UserApiModel } from '../apis/dorc-api';
import { RefDataUsersApi } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import '../icons/hardware-icons.js';

@customElement('page-users-list')
export class PageUsersList extends PageElement {
  @property({ type: Array }) users: Array<UserApiModel> = [];
  @property({ type: Array }) filteredUsers: Array<UserApiModel> = [];
  @property({ type: Array }) appConfig = [];

  @state()
  private isAddUserOrGroupDialogOpened: boolean = false;

  private loading = true;

  constructor() {
    super();

    const appConfig = new Configuration({
      basePath: new AppConfig().dorcApi
    });
    const api = new RefDataUsersApi(appConfig);
    api.refDataUsersGet().subscribe(
      (data: Array<UserApiModel>) => {
        this.setUsers(data);
      },

      (err: any) => console.error(err),
      () => console.log('done loading users')
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
          title="Add User or Group"
          style="width: 250px"
          @click="${this.addUser}"
        >
          <vaadin-icon
            icon="vaadin:user"
            style="color: cornflowerblue"
          ></vaadin-icon
          >Add User or Group...
        </vaadin-button>
      </div>
      <paper-dialog
        class="size-position"
        id="add-user-dialog"
        allow-click-through
        modal
      >
        ${this.isAddUserOrGroupDialogOpened
          ? html`<add-user-or-group id="add-user-or-group"></add-user-or-group>`
          : html`${nothing}`}
        <div style="display: flex; justify-content: flex-end">
          <vaadin-button
            dialog-confirm
            @click="${this.addUserOrGroupDialogClosed}"
            >Close</vaadin-button
          >
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
              .items=${this.filteredUsers}
              column-reordering-allowed
              multi-sort
              theme="compact row-stripes no-row-borders no-border"
            >
              <vaadin-grid-column
                .renderer="${this.renderLoginType}"
                width="50px"
                flex-grow="0"
              ></vaadin-grid-column>
              <vaadin-grid-sort-column
                path="DisplayName"
                header="Display Name"
                style="color:lightgray"
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="LanId"
                header="System Id"
              ></vaadin-grid-sort-column>
            </vaadin-grid>
          `} `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'user-or-group-created',
      this.closeAddUser as EventListener
    );
  }

  setUsers(servers: Array<UserApiModel>) {
    this.users = servers;
    this.filteredUsers = servers;
    this.loading = false;
  }

  renderLoginType(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<UserApiModel>
  ) {
    const user = model.item as UserApiModel;
    if (user.LoginType?.toLowerCase() === 'windows') {
      render(
        html`<vaadin-icon
          icon="hardware:desktop-windows"
          style="color: cornflowerblue"
        ></vaadin-icon>`,
        root
      );
    }
    if (user.LoginType?.toLowerCase() === 'endur') {
      render(
        html`<vaadin-icon
          icon="vaadin:chart-grid"
          style="color: cornflowerblue"
        ></vaadin-icon>`,
        root
      );
    }
    if (user.LoginType?.toLowerCase() === 'sql') {
      render(
        html`<vaadin-icon
          icon="vaadin:database"
          style="color: cornflowerblue"
        ></vaadin-icon>`,
        root
      );
    }
  }

  updateSearch(e: CustomEvent) {
    const value = (e.detail.value as string) || '';
    const filters = value
      .trim()
      .split('|')
      .map(filter => new RegExp(filter, 'i'));

    this.filteredUsers = this.users.filter(
      ({ DisplayName, LanId, LoginType }) =>
        filters.some(
          filter =>
            filter.test(DisplayName || '') ||
            filter.test(LanId || '') ||
            filter.test(LoginType || '')
        )
    );
  }

  addUser() {
    const attachEnv = this.shadowRoot?.getElementById(
      'add-user-dialog'
    ) as PaperDialogElement;
    attachEnv.open();
    this.isAddUserOrGroupDialogOpened = true;
  }

  closeAddUser(e: CustomEvent) {
    const dialog = this.shadowRoot?.getElementById(
      'add-user-dialog'
    ) as PaperDialogElement;
    dialog.close();
    this.isAddUserOrGroupDialogOpened = false;

    const user = e.detail.user as UserApiModel;

    const newUser: UserApiModel = {
      DisplayName: user.DisplayName,
      LanId: user.LanId,
      LoginType: user.LoginType
    };

    this.users.push(newUser);
    this.users = JSON.parse(JSON.stringify(this.users));
    this.filteredUsers.push(newUser);
    this.filteredUsers = JSON.parse(JSON.stringify(this.filteredUsers));
  }

  addUserOrGroupDialogClosed() {
    this.isAddUserOrGroupDialogOpened = false;
  }
}
