import { ComboBox } from '@vaadin/combo-box/src/vaadin-combo-box';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, LitElement, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/grid-button-groups/delegated-user-controls';
import { DelegatedUsersApi } from '../apis/dorc-api';
import { UserApiModel } from '../apis/dorc-api/models';

@customElement('attached-delegated-users')
export class AttachedDelegatedUsers extends LitElement {
  @property({ type: Array }) users: UserApiModel[] = [];

  @property({ type: Boolean }) delegatedUsersLoading = true;

  get envName(): string | undefined {
    return this._envName;
  }

  set envName(value: string | undefined) {
    this._envName = value;
    this.refreshUnallocatedUsers();
  }

  private _envName: string | undefined;

  @property({ type: Array }) unallocatedUsers: UserApiModel[] = [];

  @property({ type: Boolean }) private readonly = true;

  private selectedUnallocatedUserId: number | undefined;

  static get styles() {
    return css`
      :host {
        height: 100%;
        display: flex;
        flex-direction: column;
      }
      .grid-container {
        flex-grow: 1;
        display: flex;
        flex-direction: column;
        height: 100%;
      }
      vaadin-grid {
        flex-grow: 1;
        height: 100%;
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
      vaadin-button:disabled,
      vaadin-button[disabled] {
        background-color: #dde2e8;
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Application Users with Delegated Privileges"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px; display: flex; flex-direction: column;"
      >
        <vaadin-combo-box
          id="unallocated-users"
          item-value-path="Id"
          item-label-path="DisplayName"
          @value-changed="${this._selectedUnallocatedUserChanged}"
          .items="${this.unallocatedUsers}"
          placeholder="Select Delegate"
          style="width: 300px"
          clear-button-visible
        ></vaadin-combo-box>
        <vaadin-button
          @click="${this._addDelegate}"
          .disabled="${this.readonly}"
          >Add Delegate</vaadin-button
        >
      </vaadin-details>
      ${this.delegatedUsersLoading
        ? html` <div class="small-loader"></div> `
        : html``}
      <div class="grid-container">
        <vaadin-grid
          id="grid"
          .items="${this.users}"
          theme="compact row-stripes no-row-borders no-border"
          style="height: 100%;"
        >
          <vaadin-grid-sort-column header="Name" path="DisplayName" resizable>
          </vaadin-grid-sort-column>
          <vaadin-grid-sort-column header="Login ID" path="LoginId" resizable>
          </vaadin-grid-sort-column>
          <vaadin-grid-sort-column
            header="Login Type"
            path="LoginType"
            resizable
          >
          </vaadin-grid-sort-column>
          <vaadin-grid-sort-column header="LAN ID" path="LanId" resizable>
          </vaadin-grid-sort-column>
          <vaadin-grid-sort-column
            header="LAN ID Type"
            path="LanIdType"
            resizable
          >
          </vaadin-grid-sort-column>
          <vaadin-grid-sort-column header="Team" path="Team" resizable>
          </vaadin-grid-sort-column>
          <vaadin-grid-column
            .renderer="${this._boundUsersButtonsRenderer}"
            .attachedDelUsersControl="${this}"
            resizable
          >
          </vaadin-grid-column>
        </vaadin-grid>
      </div>
    `;
  }

  _boundUsersButtonsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<UserApiModel>
  ) {
    const user = model.item as UserApiModel;
    // The below line has a horrible hack
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const altThis = _column.attachedDelUsersControl as AttachedDelegatedUsers;
    render(
      html`<delegated-user-controls
        .user="${user}"
        .envName="${altThis.envName}"
        .readonly="${altThis.readonly}"
      ></delegated-user-controls>`,
      root
    );
  }

  refreshUnallocatedUsers() {
    if (this.envName !== '') {
      const api = new DelegatedUsersApi();
      api
        .delegatedUsersGetUnallocatedUsersGet({ envName: this.envName ?? '' })
        .subscribe(
          (data: UserApiModel[]) => {
            this.setUnallocatedUsers(data);
          },
          (err: any) => console.error(err),
          () => console.log('done getting unallocated users')
        );
    }
  }

  _selectedUnallocatedUserChanged(data: CustomEvent) {
    const combo = data.target as ComboBox;
    this.selectedUnallocatedUserId = parseInt(combo.value, 10);
  }

  _addDelegate() {
    console.log('adding delegate');
    this.delegatedUsersLoading = true;
    const api = new DelegatedUsersApi();
    api
      .delegatedUsersPost({
        userId: this.selectedUnallocatedUserId ?? 0,
        envName: this.envName ?? ''
      })
      .subscribe({
        next: () => {
          this.refreshUnallocatedUsers();
          const event = new CustomEvent('delegated-users-changed', {
            detail: {},
            bubbles: true,
            composed: true
          });
          this.dispatchEvent(event);
        },
        error: (result: any) => {
          console.log(result);
          const event = new CustomEvent('error-alert', {
            detail: { description: 'Failed to add delegate: ', result },
            bubbles: true,
            composed: true
          });
          this.dispatchEvent(event);
        },
        complete: () => {
          this.delegatedUsersLoading = false;
        }
      });
  }

  private setUnallocatedUsers(data: UserApiModel[]) {
    this.unallocatedUsers = data;
  }
}
