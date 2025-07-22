import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/combo-box';
import '@vaadin/button';
import { ComboBox, ComboBoxItemModel } from '@vaadin/combo-box';
import { customElement, property } from 'lit/decorators.js';
import './dorc-icon.js';
import { html } from 'lit/html.js';
import {
  PermissionDto,
  RefDataPermissionApi,
  RefDataUserPermissionsApi,
  RefDataUsersApi,
  UserApiModel,
  UserPermDto
} from '../apis/dorc-api';

@customElement('edit-database-permissions')
export class EditDatabasePermissions extends LitElement {
  @property({ type: Number })
  dbId = 0;

  @property({ type: Array })
  private permissions: PermissionDto[] | undefined;

  @property({ type: Array })
  private users: UserApiModel[] | undefined;

  private permissionsMap: Map<number | undefined, PermissionDto> | undefined;

  private usersMap: Map<number | undefined, UserApiModel> | undefined;

  @property({ type: Object })
  private selectedUser: UserApiModel | undefined;

  @property({ type: Object })
  private selectedPermission: PermissionDto | undefined;

  @property({ type: Boolean })
  private canSubmit = false;

  @property({ type: Array })
  private userPermissionList: UserPermDto[] = [];

  @property({ type: String })
  private StatusMessage = '';

  @property({ type: Number })
  envId = 0;

  constructor() {
    super();

    const refDataPermissionApi = new RefDataPermissionApi();
    refDataPermissionApi.refDataPermissionGet().subscribe(
      (data: PermissionDto[]) => {
        this.setPermissions(data);
      },
      (err: any) => console.error(err),
      () => console.log('done loading permissions')
    );

    const api = new RefDataUsersApi();
    api.refDataUsersGet().subscribe(
      (data: UserApiModel[]) => {
        this.setUsers(data);
      },
      (err: any) => console.error(err),
      () => console.log('done loading users')
    );
  }

  static get styles() {
    return css``;
  }

  render() {
    return html`
      <div>
        <div class="inline">
          <vaadin-combo-box
            id="users"
            label="Users"
            item-value-path="Id"
            item-label-path="DisplayName"
            @value-changed="${this.setSelectedUser}"
            .items="${this.users}"
            filter-property="DisplayName"
            .renderer="${this._boundUsersRenderer}"
            placeholder="Select User"
            style="width: 300px"
            clear-button-visible
          >
          </vaadin-combo-box>
          <vaadin-combo-box
            id="permissions"
            label="Permissions"
            item-value-path="Id"
            item-label-path="DisplayName"
            @value-changed="${this.setSelectedPermission}"
            .items="${this.permissions}"
            filter-property="DisplayName"
            .renderer="${this._boundPermissionsRenderer}"
            placeholder="Select Permission"
            style="width: 300px"
            clear-button-visible
          >
          </vaadin-combo-box>
          <vaadin-button
            raised
            .disabled="${!this.canSubmit}"
            @click="${this._addPermissionForUser}"
            >Add</vaadin-button
          >
        </div>
        <div style="background: aliceblue">
          ${this.selectedUser !== undefined
            ? html`<span
                >Current permissions for ${this.selectedUser?.DisplayName}
              </span>`
            : html``}
          <paper-listbox>
            ${this.userPermissionList.map(
              userPerm => html`
                <paper-item>
                  <span>${userPerm.Database} - ${userPerm.Role}</span>
                  <dorc-icon icon="unlink" color="#FF3131"></dorc-icon>
                </paper-item>
              `
            )}
          </paper-listbox>
        </div>
        <div>
          <span>${this.StatusMessage}</span>
        </div>
      </div>
    `;
  }

  public setDbId(dbId: number) {
    this.dbId = dbId;
  }

  resetSelectedUser() {
    this.selectedUser = undefined;
    const users = this.shadowRoot?.getElementById('users') as ComboBox;
    if (users) {
      users.selectedItem = undefined;
    }
    this.refreshUserPermissions();
    this._canSubmit();
  }

  resetSelectedPermission() {
    this.selectedPermission = undefined;
    const permissions = this.shadowRoot?.getElementById(
      'permissions'
    ) as ComboBox;
    if (permissions) {
      permissions.selectedItem = undefined;
    }
    this.refreshUserPermissions();
    this._canSubmit();
  }

  reset() {
    this.resetSelectedUser();
    this.resetSelectedPermission();
    this.StatusMessage = '';
    this.setUserPermissions([]);
  }

  setSelectedUser(data: any) {
    if (data.detail.value > 0) {
      this.selectedUser = this.usersMap?.get(data.detail.value);
      this.refreshUserPermissions();
      this._canSubmit();
    }
  }

  setSelectedPermission(data: any) {
    if (data.detail.value > 0) {
      this.selectedPermission = this.permissionsMap?.get(data.detail.value);
      this.refreshUserPermissions();
      this._canSubmit();
    }
  }

  _boundUsersRenderer(
    root: HTMLElement,
    _: HTMLElement,
    { item }: ComboBoxItemModel<UserApiModel>
  ) {
    // only render the checkbox once, to avoid re-creating during subsequent calls
    const user = item as UserApiModel;
    root.innerHTML = `<div>${user.DisplayName} - ${user.LoginType}</div>`;
  }

  _boundPermissionsRenderer(
    root: HTMLElement,
    _: HTMLElement,
    { item }: ComboBoxItemModel<PermissionDto>
  ) {
    // only render the checkbox once, to avoid re-creating during subsequent calls
    const permissionDto = item as PermissionDto;
    root.innerHTML = `<div>${permissionDto.DisplayName}</div>`;
  }

  _canSubmit() {
    this.canSubmit =
      this.selectedUser !== undefined && this.selectedPermission !== undefined;
  }

  _remove(e: { target: { data: UserPermDto } }) {
    const userPerm = e.target.data as UserPermDto;
    const removeRoleId = userPerm.Id || 0;
    const answer = confirm('Remove permission?');
    if (answer && removeRoleId) {
      const api = new RefDataUserPermissionsApi();
      const perm: number = removeRoleId;
      const user: number = this?.selectedUser?.Id || 0;
      api
        .refDataUserPermissionsDelete({
          dbId: this.dbId,
          permissionId: perm,
          userId: user,
          envId: this.envId
        })
        .subscribe(
          () => {
            this.StatusMessage = 'Permission deleted';
            this.refreshUserPermissions();
          },
          (err: any) => {
            this.StatusMessage = err.detail.response.ExceptionMessage;
          },
          () => console.log('done removing permission from user')
        );
    }
  }

  //
  // updated(changedProperties: PropertyValues) {
  //   changedProperties.forEach((oldValue, propName) => {
  //     // eslint-disable-next-line @typescript-eslint/ban-ts-comment
  //     // @ts-ignore
  //     console.log(
  //       `${propName} changed. oldValue: ${JSON.stringify(
  //         oldValue
  //       )}, newValue ${JSON.stringify(this[propName])}`
  //     );
  //   });
  // }

  _addPermissionForUser() {
    this.StatusMessage = '';
    const api = new RefDataUserPermissionsApi();
    const perm: number = this?.selectedPermission?.Id || 0;
    const user: number = this?.selectedUser?.Id || 0;
    api
      .refDataUserPermissionsPut({
        dbId: this.dbId,
        permissionId: perm,
        userId: user,
        envId: this.envId
      })
      .subscribe(
        () => {
          this.StatusMessage = 'Permission Added';
        },
        (err: any) => {
          this.StatusMessage = err.detail.response.ExceptionMessage;
        },
        () => {
          console.log(
            `done adding permission ${
              this?.selectedPermission?.DisplayName
            }for user${this.selectedUser?.DisplayName}`
          );
          this.refreshUserPermissions();
        }
      );
  }

  private refreshUserPermissions() {
    if (this.selectedUser !== undefined) {
      const userId = this.selectedUser?.Id;

      if (userId !== undefined && userId > 0) {
        const api = new RefDataUserPermissionsApi();
        api
          .refDataUserPermissionsGet({
            userId,
            databaseId: this.dbId,
            envId: this.envId
          })
          .subscribe(
            (data: UserPermDto[]) => {
              this.setUserPermissions(data);
            },
            (err: any) => {
              this.StatusMessage = err.detail.response.ExceptionMessage;
            },
            () => {
              console.log(
                `done Getting permissions for User:${
                  this.selectedUser?.DisplayName
                }`
              );
            }
          );
      }
    }
  }

  private setUserPermissions(data: UserPermDto[]) {
    this.userPermissionList = data;
  }

  private setPermissions(data: PermissionDto[]) {
    this.permissionsMap = new Map(data.map(obj => [obj.Id, obj]));
    this.permissions = data;
  }

  private setUsers(data: UserApiModel[]) {
    // const element = this.shadowRoot?.getElementById('users') as ComboBoxElement;
    this.usersMap = new Map(data.map(obj => [obj.Id, obj]));
    this.users = data;
  }
}
