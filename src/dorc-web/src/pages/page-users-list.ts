import { columnBodyRenderer } from '@vaadin/grid/lit';
import '@vaadin/dialog';
import '../components/dorc-spinner';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/text-field';
import { css, PropertyValues, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import AppConfig from '../app-config';
import '../components/add-user-or-group/add-user-or-group';
import { Configuration, UserApiModel } from '../apis/dorc-api';
import { RefDataUsersApi } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import '../icons/hardware-icons.js';
import { ref } from 'lit/directives/ref.js';
import { UnsavedChangesGuard } from '../components/unsaved-changes-guard';

@customElement('page-users-list')
export class PageUsersList extends PageElement {
  private readonly unsavedChanges = new UnsavedChangesGuard();

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
            style="color: var(--dorc-link-color)"
          ></vaadin-icon
          >Add User or Group...
        </vaadin-button>
      </div>
      <vaadin-dialog
        ${ref(this.unsavedChanges.attach)}
        id="add-user-dialog"
        header-title="Add User or Group"
        draggable
        .opened="${this.isAddUserOrGroupDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.isAddUserOrGroupDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderAddUser, [
          this.isAddUserOrGroupDialogOpened
        ])}
        ${dialogFooterRenderer(this.renderAddUserFooter, [])}
      ></vaadin-dialog>
      ${
        this.loading
          ? html` <dorc-spinner></dorc-spinner> `
          : html`
              <vaadin-grid
                id="grid"
                .items=${this.filteredUsers}
                column-reordering-allowed
                multi-sort
                theme="compact row-stripes no-row-borders no-border"
              >
                <vaadin-grid-column
                  ${columnBodyRenderer(this.renderLoginType, [])}
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
            `
      } `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    // KNOWN DEFECT (pre-existing): `add-user-or-group` dispatches
    // `user-or-group-created` with `composed: true` but no `bubbles`, so this
    // listener never fires and `closeAddUser` is dead code. Adding `bubbles`
    // activates it, and `closeAddUser` then dereferences `e.detail.user`
    // unconditionally — which fails. Fixing it needs its own change with its
    // own tests; it is deliberately NOT bundled into the dialog conversion.
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

  renderLoginType(user: UserApiModel) {
    if (user.LoginType?.toLowerCase() === 'windows') {
      return html`<vaadin-icon
        icon="hardware:desktop-windows"
        style="color: var(--dorc-link-color)"
      ></vaadin-icon>`;
    }
    if (user.LoginType?.toLowerCase() === 'endur') {
      return html`<vaadin-icon
        icon="vaadin:chart-grid"
        style="color: var(--dorc-link-color)"
      ></vaadin-icon>`;
    }
    if (user.LoginType?.toLowerCase() === 'sql') {
      return html`<vaadin-icon
        icon="vaadin:database"
        style="color: var(--dorc-link-color)"
      ></vaadin-icon>`;
    }
    // Any other login type shows nothing, as it did when the imperative
    // renderer fell through without calling render().
    return nothing;
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

  /**
   * The open flag also gates the content, so `<add-user-or-group>` is torn down
   * and rebuilt on each open — a fresh, empty form. Vaadin caches the renderer
   * root, so without this gate (and the flag in the dependency array) the
   * previous form state would persist across reopen.
   */
  private renderAddUser = () =>
    this.isAddUserOrGroupDialogOpened
      ? html`<add-user-or-group id="add-user-or-group"></add-user-or-group>`
      : nothing;

  private renderAddUserFooter = () => html`
    <vaadin-button @click="${() => (this.isAddUserOrGroupDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  addUser() {
    this.isAddUserOrGroupDialogOpened = true;
  }

  closeAddUser(e: CustomEvent) {
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
