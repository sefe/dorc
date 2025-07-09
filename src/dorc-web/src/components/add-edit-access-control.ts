import '@polymer/paper-dialog';
import { PaperDialogElement } from '@polymer/paper-dialog';
import '@vaadin/button';
import '@vaadin/checkbox';
import { Checkbox } from '@vaadin/checkbox';
import '@vaadin/combo-box';
import { ComboBox, ComboBoxItemModel } from '@vaadin/combo-box';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import { GridItemModel } from '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column.js';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/text-field';
import { TextField } from '@vaadin/text-field';
import '@vaadin/vertical-layout';
import { css, LitElement, render } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/grid-button-groups/access-control-controls';
import {
  AccessSecureApiModel,
  UserElementApiModel
} from '../apis/dorc-api';
import { AccessControlApi } from '../apis/dorc-api';
import { AccessControlApiModel } from '../apis/dorc-api';
import '@vaadin/notification';
import { ErrorNotification } from './notifications/error-notification';
import { Notification } from '@vaadin/notification';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';

const AC_ALLOW_WRITE = 1;
const AC_ALLOW_READ_SECRETS = 2;
const AC_ALLOW_OWNER = 4;

@customElement('add-edit-access-control')
export class AddEditAccessControl extends LitElement {
  @property({ type: String }) secureName = '';

  @property({ type: Boolean })
  canSubmit = false;

  @property() ErrorMessage = '';

  @property({ type: Array })
  Privileges?: Array<AccessControlApiModel>;

  searchADValue = '';

  @property({ type: Array }) searchResults!: UserElementApiModel[];

  @property({ type: Boolean }) searchingUsers = false;

  @property({ type: String }) selectedUser!: string;

  @property({ type: Boolean }) savingAccessControls = false;

  private AccessControls!: AccessSecureApiModel;

  @state()
  UserEditable = false;

  @state()
  private loading = true;

  static get styles() {
    return css`
      vaadin-text-field {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 400px;
        padding: 5px;
      }
      vaadin-combo-box {
        --lumo-space-m: 0px;
        width: 400px;
        padding: 5px;
      }
      .tooltip {
        position: relative;
        display: inline-block;
      }
      .tooltip .tooltiptext {
        visibility: hidden;
        width: 300px;
        background-color: black;
        color: #fff;
        text-align: center;
        border-radius: 6px;
        padding: 5px 0;

        /* Position the tooltip */
        position: absolute;
        z-index: 1;
      }
      .tooltip:hover .tooltiptext {
        visibility: visible;
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
    return html`
      <paper-dialog
        class="size-position"
        id="add-access-control-dialog"
        allow-click-through
        modal
      >
        <table>
          <tr>
            <td>
              ${this.UserEditable
                ? html`
                    <vaadin-button theme="icon">
                      <vaadin-icon
                        icon="vaadin:unlock"
                        style="color: cornflowerblue"
                      ></vaadin-icon>
                    </vaadin-button>
                  `
                : html`
                    <vaadin-button theme="icon">
                      <vaadin-icon
                        icon="vaadin:lock"
                        style="color: cornflowerblue"
                      ></vaadin-icon>
                    </vaadin-button>
                  `}
            </td>
            <td>
              <h2>${this.secureName}</h2>
              ${this.loading
                ? html` <div class="small-loader"></div> `
                : html``}
            </td>
          </tr>
        </table>
        <div style="padding-left: 10px;padding-right: 10px; width:600px">
          <vaadin-details
            opened
            summary="Add New User"
            style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; width: 100%"
          >
            <table>
              <tr>
                <td style="display: table-cell; vertical-align: bottom;">
                  <vaadin-text-field
                    id="search-criteria"
                    label="Search Criteria"
                    @input="${this.updateSearchCriteria}"
                  ></vaadin-text-field>
                </td>
                <td style="display: table-cell; vertical-align: bottom;">
                  <vaadin-button
                    @click="${this.searchAD}"
                    style="margin-bottom: 5px"
                    >Search</vaadin-button
                  >
                </td>
                <td style="display: table-cell; vertical-align: center;">
                  ${this.searchingUsers
                    ? html` <div class="small-loader"></div> `
                    : html``}
                </td>
              </tr>
              <tr>
                <td style="display: table-cell; vertical-align: bottom;">
                  <vaadin-combo-box
                    id="searchResults"
                    label="Search Results"
                    item-value-path="DisplayName"
                    item-label-path="DisplayName"
                    .items="${this.searchResults}"
                    .renderer="${this.searchResultsRenderer}"
                    @value-changed="${this.searchResultsValueChanged}"
                  ></vaadin-combo-box>
                </td>
                <td style="display: table-cell; vertical-align: bottom;">
                  <vaadin-button
                    @click="${this.addUser}"
                    style="margin-bottom: 5px"
                    ?disabled="${!this.UserEditable}"
                    >Add</vaadin-button
                  >
                </td>
              </tr>
            </table>
          </vaadin-details>
          <vaadin-grid
            .items="${this.Privileges}"
            theme="compact row-stripes no-row-borders no-border"
          >
            <vaadin-grid-sort-column
              path="Name"
              header="Name"
              resizable
              auto-width
            ></vaadin-grid-sort-column>
            <vaadin-grid-column
              header="Write"
              .renderer="${this.acCanWrite}"
              .altThis="${this}"
              resizable
              auto-width
            ></vaadin-grid-column>
            <vaadin-grid-column
              header="Read Secrets"
              .renderer="${this.acCanReadSecrets}"
              .altThis="${this}"
              resizable
              auto-width
            ></vaadin-grid-column>
            <vaadin-grid-column
              header="Owner"
              .renderer="${this.acCanOwner}"
              .altThis="${this}"
              resizable
              auto-width
            ></vaadin-grid-column>
            <vaadin-grid-column
              .renderer="${this._boundACButtonsRenderer}"
              .ACControl="${this}"
              resizable
              auto-width
            ></vaadin-grid-column>
          </vaadin-grid>

          <div style="margin-right: 30px">
            <vaadin-button
              ?disabled="${!this.UserEditable}"
              @click="${this.save}"
              >Save</vaadin-button
            >
            ${this.savingAccessControls
              ? html` <div class="small-loader"></div> `
              : html``}
          </div>
          <div style="color: #FF3131">${this.ErrorMessage}</div>
        </div>
        <div style="display: flex; justify-content: flex-end">
          <vaadin-button dialog-confirm @click="${this.close}"
            >Close</vaadin-button
          >
        </div>
      </paper-dialog>
    `;
  }

  protected override firstUpdated(): void {
    const field = this.shadowRoot?.getElementById(
      'search-criteria'
    ) as TextField;
    field.addEventListener('keydown', this.isCriteriaReady as EventListener);

    this.addEventListener(
      'access-control-search-criteria-ready',
      this.searchAD as EventListener
    );
  }

  private isCriteriaReady(e: KeyboardEvent) {
    if (e.code === 'Enter') {
      const event = new CustomEvent('access-control-search-criteria-ready', {
        detail: {
          message: 'Access Control Search Criteria Ready!'
        },
        bubbles: true,
        composed: true
      });
      this.dispatchEvent(event);
    }
    console.log(e.code);
  }

  _boundACButtonsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<AccessControlApiModel>
  ) {
    const accessControl = model.item as AccessControlApiModel;

    // The below line has a horrible hack
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const altThis = _column.ACControl as AddEditAccessControl;
    render(
      html`<access-control-controls
        .accessControl="${accessControl}"
        .disabled="${!altThis.UserEditable || model.item.Allow === AC_ALLOW_OWNER}"
        @access-control-removed="${() => {
          altThis.removeAccessControl(accessControl);
        }}"
      ></access-control-controls>`,
      root
    );
  }

  removeItem<T>(arr: Array<T>, value: T): Array<T> {
    const index = arr.indexOf(value);
    if (index > -1) {
      arr.splice(index, 1);
    }
    return arr;
  }

  removeAccessControl(accessControl: AccessControlApiModel) {
    const actual = this.Privileges?.find(value =>
      value.Id === accessControl.Id &&
      value.Pid === accessControl.Pid &&
      value.Sid === accessControl.Sid
    );

    if (actual !== undefined) {
      const splicedArray = this.removeItem(this.Privileges ?? [], actual);

      this.Privileges = JSON.parse(JSON.stringify(splicedArray));
    }
  }

  save() {
    this.savingAccessControls = true;

    const ac: AccessSecureApiModel = {
      Name: this.AccessControls.Name,
      Privileges: this.Privileges,
      Type: this.AccessControls.Type,
      UserEditable: this.UserEditable,
      ObjectId: this.AccessControls.ObjectId
    };

    const api = new AccessControlApi();
    api.accessControlPut({ accessSecureApiModel: ac }).subscribe({
      next: (data: AccessSecureApiModel) => {
        this.AccessControls = data;
        this.Privileges =
          data.Privileges !== null ? data.Privileges : undefined;
        this.savingAccessControls = false;
      },
      error: (err: any) => {
        const notification = new ErrorNotification();
        notification.setAttribute('errorMessage', err.response);
        this.shadowRoot?.appendChild(notification);
        notification.open();
        console.log(err);
        this.savingAccessControls = false;
      },
      complete: () => {
        console.log('completed saving updated access controls');
        this.close();
        Notification.show(`Access controls updated successfully`, {
          theme: 'success',
          position: 'bottom-start',
          duration: 3000
        });
      }
    });
  }

  sortAccessControls(a: AccessControlApiModel, b: AccessControlApiModel) {
    const nameA: string =
      a.Name !== undefined && a.Name !== null ? a.Name?.toUpperCase() : ''; // ignore upper and lowercase
    const nameB: string =
      b.Name !== undefined && b.Name !== null ? b.Name?.toUpperCase() : ''; // ignore upper and lowercase
    if (nameA < nameB) {
      return -1;
    }
    if (nameA > nameB) {
      return 1;
    }
    // names must be equal
    return 0;
  }

  searchResultsValueChanged(data: CustomEvent<any>) {
    this.selectedUser = data.detail.value;
  }

  addUser() {
    const user = this.searchResults?.find(
      p => p.DisplayName === this.selectedUser
    );

    if (user !== undefined) {
      const acam: AccessControlApiModel = {
        Name: user.DisplayName,
        Allow: 0,
        Deny: 0,
        Pid: user.Pid,
        Sid: user.Sid,
      };
      this.Privileges?.push(acam);
      this.Privileges = JSON.parse(JSON.stringify(this.Privileges));
    }
  }

  searchResultsRenderer(
    root: HTMLElement,
    _comboBox: ComboBox,
    model: ComboBoxItemModel<UserElementApiModel>
  ) {
    render(
      html`<vaadin-vertical-layout>
        <div style="line-height: var(--lumo-line-height-m);">
          ${model.item.DisplayName ?? ''}
        </div>
        <div
          style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
        >
          ${model.item.Username ?? ''}
        </div>
      </vaadin-vertical-layout>`,
      root
    );
  }

  updateSearchCriteria(data: any) {
    this.searchADValue = data.currentTarget.value;
  }

  searchAD() {
    this.searchingUsers = true;
    const api = new AccessControlApi();
    api.accessControlSearchUsersGet({ search: this.searchADValue }).subscribe(
      (data: Array<UserElementApiModel>) => {
        this.searchResults = data;
        this.searchingUsers = false;
        const combo = this.shadowRoot?.getElementById(
          'searchResults'
        ) as ComboBox;
        if (combo) combo.open();
      },
      (err: any) => console.error(err),
      () => console.log('Finished searching Active Directory')
    );
  }

  acCanReadSecrets(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<AccessControlApiModel>
  ) {
    // The below line has a horrible hack
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const addEditAccessControl = _column.altThis as AddEditAccessControl;

    const canReadSecrets =
      ((model.item.Allow ?? 0) & AC_ALLOW_READ_SECRETS) > 0;

    render(
      html`<vaadin-checkbox
        ?disabled="${!addEditAccessControl.UserEditable}"
        .checked="${canReadSecrets}"
      ></vaadin-checkbox>`,
      root
    );

    const checkbox: Checkbox = root.querySelector(
      'vaadin-checkbox'
    ) as Checkbox;

    checkbox.addEventListener('checked-changed', (e: CustomEvent) => {
      const canReadSecretsLocal =
        ((model.item.Allow ?? 0) & AC_ALLOW_READ_SECRETS) > 0;

      const checked = e.detail.value as boolean;
      if (checked && !canReadSecretsLocal) {
        if (model.item.Allow !== undefined) {
          model.item.Allow |= AC_ALLOW_READ_SECRETS;
        }
      }
      if (!checked && canReadSecretsLocal) {
        if (model.item.Allow !== undefined) {
          model.item.Allow ^= AC_ALLOW_READ_SECRETS;
        }
      }
      console.log(`for ${model.item.Name} setting to ${model.item.Allow}`);
    });
  }

  acCanWrite(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<AccessControlApiModel>
  ) {
    // The below line has a horrible hack
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const addEditAccessControl = _column.altThis as AddEditAccessControl;

    const canWriteRender = ((model.item.Allow ?? 0) & AC_ALLOW_WRITE) > 0;

    render(
      html`<vaadin-checkbox
        ?disabled="${!addEditAccessControl.UserEditable}"
        ?checked="${canWriteRender}"
      ></vaadin-checkbox>`,
      root
    );

    const checkbox: Checkbox = root.querySelector(
      'vaadin-checkbox'
    ) as Checkbox;

    checkbox.addEventListener('checked-changed', (e: any) => {
      const canWrite = ((model.item.Allow ?? 0) & AC_ALLOW_WRITE) > 0;

      const checked = e.detail.value as boolean;
      if (checked && !canWrite) {
        if (model.item.Allow !== undefined) {
          model.item.Allow |= AC_ALLOW_WRITE;
        }
      }
      if (!checked && canWrite) {
        if (model.item.Allow !== undefined) {
          model.item.Allow ^= AC_ALLOW_WRITE;
        }
      }
      console.log(`for ${model.item.Name} setting to ${model.item.Allow}`);
    });
  }

  acCanOwner(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<AccessControlApiModel>
  ) {
    const canOwnerRender = ((model.item.Allow ?? 0) & AC_ALLOW_OWNER) > 0;

    render(
      html`<vaadin-checkbox
        ?disabled="${true}"
        ?checked="${canOwnerRender}"
      ></vaadin-checkbox>`,
      root
    );
  }


  setTextField(id: string, value: string) {
    const textField = this.shadowRoot?.getElementById(id) as TextField;
    if (textField) textField.value = value;
  }

  open(secureName: string, secureType: number) {
    const dialog = this.shadowRoot?.getElementById(
      'add-access-control-dialog'
    ) as PaperDialogElement;
    this.loading = true;

    if (secureName !== '') {
      const api = new AccessControlApi();
      api
        .accessControlGet({
          accessControlType: secureType,
          accessControlName: secureName
        })
        .subscribe({
          next: (data: AccessSecureApiModel) => {
            data.Privileges = data.Privileges?.sort(this.sortAccessControls);
            this.Privileges = data.Privileges;
            this.UserEditable = data.UserEditable ?? false;
            this.AccessControls = data;

            this.loading = false;
          },
          error: (err: string) => console.error(err),
          complete: () => console.log('finished loading access controls')
        });
    }
    this.secureName = secureName;

    dialog.open();
    this.ErrorMessage = '';
  }

  close() {
    const dialog = this.shadowRoot?.getElementById(
      'add-access-control-dialog'
    ) as PaperDialogElement;
    dialog.close();
    this.Privileges = [];
    this.ErrorMessage = '';
    this.setTextField('search-criteria', '');
    const searchResult = this.shadowRoot?.getElementById(
      'searchResults'
    ) as ComboBox;
    if (searchResult) searchResult.selectedItem = undefined;
  }
}
