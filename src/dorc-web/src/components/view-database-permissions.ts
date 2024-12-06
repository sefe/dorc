import '@vaadin/item';
import '@vaadin/list-box';
import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { styleMap } from 'lit/directives/style-map.js';
import {
  RefDataDatabaseUsersApi,
  RefDataUserPermissionsApi,
  UserApiModel,
  UserPermDto
} from '../apis/dorc-api';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';

@customElement('view-database-permissions')
export class ViewDatabasePermissions extends LitElement {
  @property({ type: Number })
  dbId = 0;

  @property({ type: Array })
  private users: UserApiModel[] | undefined;

  @property({ type: String })
  private StatusMessage = '';

  @property({ type: Array })
  private userPermissionList: UserPermDto[] = [];

  @property({ type: Object })
  private selectedUser: UserApiModel | undefined;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) private readonly = true;

  @property({ type: Boolean }) private loading = true;

  static get styles() {
    return css`
      vaadin-item {
        padding: 0px;
        margin: 0px;
      }
      .small-loader {
        border: 2px solid #f3f3f3; /* Light grey */
        border-top: 2px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
      }
      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }
    `;
  }

  render() {
    const unlinkStyles = {
      color: this.readonly ? 'grey' : '#FF3131'
    };
    return html`
      <div>            
      <span>Select user to see permissions</span>
        ${this.loading ? html` <div class="small-loader"></div> ` : html``}
        <table>
          <tr>
            <td>
              <vaadin-list-box style="height: 200px; width:300px; border: 1px solid lightgray">
                ${this.users?.map(
                  user =>
                    html` <vaadin-item
                      @click="${this._listPermissions}"
                      .data="${user}"
                    >
                      ${user.DisplayName}
                    </vaadin-item>`
                )}
              </vaadin-list-box>
            </td>
            <td>
              <vaadin-list-box style="height: 200px; width:300px; border: 1px solid lightgray">
              ${this.userPermissionList.map(
                userPerm =>
                  html` <vaadin-item>
                    ${userPerm.Role}
                    <vaadin-button
                      title="Manage permissions"
                      theme="icon"
                      @click="${this._remove}"
                      ?disabled="${this.readonly}"
                    >
                      <vaadin-icon
                        icon="vaadin:unlink"
                        style=${styleMap(unlinkStyles)}
                        .data="${userPerm}"
                      ></vaadin-icon>
                    </vaadin-button>
                  </vaadin-item>`
              )}
              </vaadin-list-box>
            </td>
          </tr> 
        </table>
        </div>
        <br>
        <span>${this.StatusMessage}</span>
        </div> 
    `;
  }

  public setDbId(dbId: number) {
    this.dbId = dbId;
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
            this.loadUserPerms();
          },
          (err: any) => {
            this.StatusMessage = err.response.ExceptionMessage;
          },
          () => console.log('done removing permission from user')
        );
    }
  }

  public loadDatabaseUsers() {
    if (this.dbId > 0) {
      this.loading = true;
      const refDataPermissionApi = new RefDataDatabaseUsersApi();
      refDataPermissionApi
        .refDataDatabaseUsersGet({ id: this.dbId, envId: this.envId })
        .subscribe(
          (data: UserApiModel[]) => {
            this.setDatabaseUsers(data);
            this.loading = false;
          },
          (err: any) => console.error(err),
          () => console.log('done loading database users')
        );
    }
  }

  private setDatabaseUsers(data: UserApiModel[]) {
    this.users = data.sort(this.sortUsers);
  }

  sortUsers(a: UserApiModel, b: UserApiModel): number {
    if (String(a.DisplayName) > String(b.DisplayName)) return 1;

    return -1;
  }

  private _listPermissions(e: any) {
    this.selectedUser = e.currentTarget.data as UserApiModel;
    this.userPermissionList = [];
    this.loadUserPerms();
  }

  private loadUserPerms() {
    const api = new RefDataUserPermissionsApi();
    api
      .refDataUserPermissionsGet({
        userId: this.selectedUser?.Id || 0,
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

  private setUserPermissions(data: UserPermDto[]) {
    this.userPermissionList = data;
  }
}
