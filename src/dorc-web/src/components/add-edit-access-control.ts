import { live } from 'lit/directives/live.js';
import { columnBodyRenderer } from '@vaadin/grid/lit';
import { comboBoxRenderer } from '@vaadin/combo-box/lit';
import '@vaadin/dialog';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { ref } from 'lit/directives/ref.js';
import '@vaadin/button';
import '@vaadin/checkbox';
import { Checkbox } from '@vaadin/checkbox';
import '@vaadin/combo-box';
import { ComboBox } from '@vaadin/combo-box';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/text-field';
import { TextField } from '@vaadin/text-field';
import '@vaadin/vertical-layout';
import { LitElement, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/grid-button-groups/access-control-controls';
import { AccessSecureApiModel, UserElementApiModel } from '../apis/dorc-api';
import { AccessControlApi, AccessControlType } from '../apis/dorc-api';
import { AccessControlApiModel } from '../apis/dorc-api';
import '@vaadin/notification';
import { ErrorNotification } from './notifications/error-notification';
import { Notification } from '@vaadin/notification';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

const AC_ALLOW_WRITE = 1;
const AC_ALLOW_READ_SECRETS = 2;
const AC_ALLOW_OWNER = 4;

@customElement('add-edit-access-control')
export class AddEditAccessControl extends LitElement {
  @state() private dialogOpened = false;

  @property({ type: String }) secureName = '';

  @property({ type: Boolean })
  canSubmit = false;

  @property() ErrorMessage = '';

  @property({ type: Array })
  Privileges?: Array<AccessControlApiModel>;

  searchADValue = '';

  @property({ type: Array }) searchResults!: UserElementApiModel[];

  @property({ type: Boolean }) searchingUsers = false;

  @property({ type: Boolean }) savingAccessControls = false;

  private AccessControls!: AccessSecureApiModel;

  @state()
  UserEditable = false;

  @state()
  UserIsOwner = false;

  @state()
  UserCanReadSecrets = false;

  @state()
  private loading = true;

  private _ownerLimitNotified = false;

  static get styles() {
    return css`
      vaadin-dialog::part(overlay) {
        overflow: auto;
        width: min(90vw, 650px);
      }
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
        display: inline-block;
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
      .dialog-actions {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-m, 1rem);
      }
      .save-action {
        display: inline-flex;
        align-items: center;
        gap: var(--lumo-space-xs, 0.375rem);
      }
      .save-progress {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 16px;
        height: 16px;
      }
    `;
  }

  private acStyles = {
    displayName: `color: var(--lumo-body-text-color);`,
    username: `
      font-size: var(--lumo-font-size-s);
      color: var(--lumo-secondary-text-color);`,
    additionalId: `
      font-size: var(--lumo-font-size-xs);
      color: var(--lumo-tertiary-text-color);
      font-style: italic;
      opacity: 0.8;`
  };

  render() {
    return html`
      <vaadin-dialog
        id="add-access-control-dialog"
        header-title="Access Control"
        draggable
        no-close-on-esc
        no-close-on-outside-click
        .opened="${this.dialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.dialogOpened = e.detail.value;
          if (!this.dialogOpened) this.resetDialogState();
        }}"
        ${dialogRenderer(this.renderAccessControlContent, [
          // All three permission flags belong here, not just UserEditable: the
          // grid's column directives are nested inside this template, and they
          // only re-run when Lit re-renders it.
          this.UserEditable,
          this.UserIsOwner,
          this.UserCanReadSecrets,
          this.secureName,
          this.loading,
          this.Privileges,
          this.ErrorMessage,
          this.savingAccessControls,
          this.searchResults,
          this.searchingUsers
        ])}
        ${dialogFooterRenderer(this.renderAccessControlFooter, [
          this.UserEditable,
          this.savingAccessControls,
          this.Privileges
        ])}
      ></vaadin-dialog>
    `;
  }

  protected override firstUpdated(): void {
    // The search field lives inside the dialog renderer now, so it does not
    // exist until the dialog opens. Its keydown listener is attached by
    // `wireSearchCriteria` via `ref` when the field is created.
    this.addEventListener(
      'access-control-search-criteria-ready',
      this.searchAD as EventListener
    );
  }

  private wireSearchCriteria = (el?: Element) => {
    if (!el) return;
    el.removeEventListener('keydown', this.isCriteriaReady as EventListener);
    el.addEventListener('keydown', this.isCriteriaReady as EventListener);
  };

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

  _boundACButtonsRenderer(item: AccessControlApiModel) {
    const accessControl = item as AccessControlApiModel;

    return html`<access-control-controls
      .accessControl="${accessControl}"
      .disabled="${!this.UserEditable || item.Allow === AC_ALLOW_OWNER}"
      @access-control-removed="${(e: CustomEvent) => {
          // The row comes from the event, not from this closure: the closure is
          // replaced whenever the cell re-renders, which can happen while the
          // confirmation dialog is open.
          this.removeAccessControl(e.detail.accessControl);
        }}"
    ></access-control-controls>`;
  }

  removeItem<T>(arr: Array<T>, value: T): Array<T> {
    const index = arr.indexOf(value);
    if (index > -1) {
      arr.splice(index, 1);
    }
    return arr;
  }

  removeAccessControl(accessControl: AccessControlApiModel) {
    const actual = this.Privileges?.find(
      value =>
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
    if (this.savingAccessControls) {
      return;
    }

    this.savingAccessControls = true;

    const ac: AccessSecureApiModel = {
      Name: this.AccessControls.Name,
      Privileges: this.Privileges,
      Type: this.AccessControls.Type,
      UserEditable: this.UserEditable,
      ObjectId: this.AccessControls.ObjectId
    };

    const api = new AccessControlApi(dorcApiConfiguration);
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

  addUser() {
    const cbSelectedUser = this.shadowRoot?.getElementById(
      'searchResults'
    ) as ComboBox;
    const user = cbSelectedUser.selectedItem;

    if (user !== undefined) {
      const existing = this.Privileges?.find(
        item =>
          (item.Sid && item.Sid === user.Sid) ||
          (item.Pid && item.Pid === user.Pid)
      );
      if (existing) {
        Notification.show(`User is already in the list`, {
          theme: 'warning',
          position: 'bottom-start',
          duration: 3000
        });
        return;
      }

      const acam: AccessControlApiModel = {
        Name: user.DisplayName,
        Allow: 0,
        Deny: 0,
        Pid: user.Pid,
        Sid: user.Sid
      };
      this.Privileges?.push(acam);
      this.Privileges = JSON.parse(JSON.stringify(this.Privileges));
    }
  }

  searchResultsRenderer = (item: UserElementApiModel) => {
    if (!item) {
      return html``;
    }

    const { DisplayName, Username } = item;
    const displayName = DisplayName ?? '';
    const username = Username ?? '';

    return html`
      <vaadin-vertical-layout style="padding: 4px 0; gap: 0;">
        <div style="${this.acStyles.displayName}">${displayName}</div>
        <div style="${this.acStyles.username}">${username}</div>
        ${this.renderUserId(item)}
      </vaadin-vertical-layout>
    `;
  };

  renderUserId(item: UserElementApiModel): unknown {
    if (!item) {
      return html``;
    }
    const pid = item.Pid ?? '';
    const sid = item.Sid ?? '';

    const hasAdditionalId = pid && pid !== item.Username;
    const additionalId = hasAdditionalId ? pid : sid;

    return additionalId
      ? html` <div style="${this.acStyles.additionalId}">${additionalId}</div> `
      : html``;
  }

  updateSearchCriteria(data: any) {
    this.searchADValue = data.currentTarget.value;
  }

  searchAD() {
    this.searchingUsers = true;
    const api = new AccessControlApi(dorcApiConfiguration);
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

  // `change` rather than `checked-changed`, which is a notify event that also
  // fires when Lit commits the property. Cells are recycled, so committing the
  // next row's value fires it into the previous row's listener — and these
  // handlers mutate the privilege the dialog saves. `change` is gesture-only.
  acCanReadSecrets(item: AccessControlApiModel) {
    return html`<vaadin-checkbox
      ?disabled="${!this.UserEditable || !this.UserCanReadSecrets}"
      .checked="${live(((item.Allow ?? 0) & AC_ALLOW_READ_SECRETS) > 0)}"
      @change="${(e: Event) =>
        this.togglePrivilege(
          item,
          AC_ALLOW_READ_SECRETS,
          (e.currentTarget as Checkbox).checked
        )}"
    ></vaadin-checkbox>`;
  }

  /**
   * Flips one permission bit on the row's model in place.
   *
   * The grid's items are edited directly and read back on save, so this
   * deliberately mutates rather than replacing the item — the same thing the
   * imperative renderers did through their `checked-changed` listeners.
   */
  private togglePrivilege(
    item: AccessControlApiModel,
    bit: number,
    checked: boolean
  ) {
    if (item.Allow === undefined) return;
    const isSet = (item.Allow & bit) > 0;
    if (checked && !isSet) item.Allow |= bit;
    if (!checked && isSet) item.Allow ^= bit;
  }

  acNameRenderer = (item: AccessControlApiModel) => {
    const name = item.Name ?? '';

    return html`
      <div style="padding: 4px 0;">
        <div style="${this.acStyles.displayName}">${name}</div>
        ${this.renderUserId(item)}
      </div>
    `;
  };

  acCanWrite(item: AccessControlApiModel) {
    return html`<vaadin-checkbox
      ?disabled="${!this.UserEditable}"
      .checked="${live(((item.Allow ?? 0) & AC_ALLOW_WRITE) > 0)}"
      @change="${(e: Event) =>
        this.togglePrivilege(
          item,
          AC_ALLOW_WRITE,
          (e.currentTarget as Checkbox).checked
        )}"
    ></vaadin-checkbox>`;
  }

  acCanOwner(item: AccessControlApiModel) {
    return html`<vaadin-checkbox
      ?disabled="${!this.UserIsOwner}"
      .checked="${live(((item.Allow ?? 0) & AC_ALLOW_OWNER) > 0)}"
      @change="${(e: Event) =>
        this.toggleOwner(
          e.currentTarget as Checkbox,
          item,
          (e.currentTarget as Checkbox).checked
        )}"
    ></vaadin-checkbox>`;
  }

  /** Owner is capped at two per environment, so it cannot use togglePrivilege. */
  private toggleOwner(
    checkbox: Checkbox,
    item: AccessControlApiModel,
    checked: boolean
  ) {
    if (item.Allow === undefined) return;
    const isOwner = (item.Allow & AC_ALLOW_OWNER) > 0;

    if (checked && !isOwner) {
      const ownerCount =
        this.Privileges?.filter(p => ((p.Allow ?? 0) & AC_ALLOW_OWNER) > 0)
          .length ?? 0;
      if (ownerCount >= 2) {
        checkbox.checked = false;
        if (!this._ownerLimitNotified) {
          this._ownerLimitNotified = true;
          Promise.resolve().then(() => {
            this._ownerLimitNotified = false;
          });
          Notification.show('Maximum of 2 owners allowed per environment', {
            theme: 'warning',
            position: 'bottom-start',
            duration: 3000
          });
        }
        return;
      }
      item.Allow |= AC_ALLOW_OWNER;
    }

    if (!checked && isOwner) {
      item.Allow ^= AC_ALLOW_OWNER;
    }
  }

  setTextField(id: string, value: string) {
    const textField = this.shadowRoot?.getElementById(id) as TextField;
    if (textField) textField.value = value;
  }

  open(secureName: string, secureType: AccessControlType) {
    this.loading = true;

    if (secureName !== '') {
      const api = new AccessControlApi(dorcApiConfiguration);
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
            this.UserIsOwner = data.UserIsOwner ?? false;
            this.UserCanReadSecrets = data.UserCanReadSecrets ?? false;
            this.AccessControls = data;

            this.loading = false;
          },
          error: (err: string) => {
            this.loading = false;
            console.error(err);
          },
          complete: () => console.log('finished loading access controls')
        });
    }
    this.secureName = secureName;

    this.dialogOpened = true;
    this.ErrorMessage = '';
  }

  private renderAccessControlContent = () => html`
    <table>
      <tr>
        <td>
          ${
                this.UserEditable
                  ? html`
                      <vaadin-icon
                        icon="vaadin:unlock"
                        role="img"
                        aria-label="Editable"
                        title="Editable"
                        style="color: var(--dorc-link-color)"
                      ></vaadin-icon>
                    `
                  : html`
                      <vaadin-icon
                        icon="vaadin:lock"
                        role="img"
                        aria-label="Read-only"
                        title="Read-only"
                        style="color: var(--dorc-link-color)"
                      ></vaadin-icon>
                    `
              }
        </td>
        <td>
          <h2>${this.secureName}</h2>
          ${this.loading ? html` <div class="small-loader"></div> ` : html``}
        </td>
      </tr>
    </table>
    <div style="padding-left: 10px;padding-right: 10px;">
      <vaadin-details
        opened
        summary="Add New User"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; width: 100%"
      >
        <table>
          <tr>
            <td style="display: table-cell; vertical-align: bottom;">
              <vaadin-text-field
                id="search-criteria"
                label="Search Criteria"
                @input="${this.updateSearchCriteria}"
                ${ref(this.wireSearchCriteria)}
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
              ${
                    this.searchingUsers
                      ? html` <div class="small-loader"></div> `
                      : html``
                  }
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
                ${comboBoxRenderer(this.searchResultsRenderer, [])}
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
        style="width: 100%;"
      >
        <vaadin-grid-sort-column
          header="Name"
          ${columnBodyRenderer(this.acNameRenderer, [])}
          flex="3"
          resizable
          auto-width
        ></vaadin-grid-sort-column>
        <vaadin-grid-column
          header="Write"
          ${columnBodyRenderer(this.acCanWrite, [this.UserEditable])}
          flex="1"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="Read Secrets"
          ${columnBodyRenderer(this.acCanReadSecrets, [this.UserEditable, this.UserCanReadSecrets])}
          flex="1"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="Owner"
          ${columnBodyRenderer(this.acCanOwner, [this.UserIsOwner])}
          flex="1"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="Actions"
          ${columnBodyRenderer(this._boundACButtonsRenderer, [
                this.UserEditable
              ])}
          flex="1"
          resizable
          auto-width
        ></vaadin-grid-column>
      </vaadin-grid>

      <div style="color: var(--dorc-error-color)">${this.ErrorMessage}</div>
    </div>
  `;

  private renderAccessControlFooter = () => html`
    <div class="dialog-actions">
      <div class="save-action">
        <vaadin-button
          id="save-access-controls"
          ?disabled="${!this.UserEditable || this.savingAccessControls}"
          @click="${this.save}"
          >Save</vaadin-button
        >
        <span class="save-progress" aria-live="polite">
          ${
                this.savingAccessControls
                  ? html`<span
                      class="small-loader"
                      aria-label="Saving access controls"
                    ></span>`
                  : html``
              }
        </span>
      </div>
      <vaadin-button id="close-access-controls" @click="${this.close}"
        >Close</vaadin-button
      >
    </div>
  `;

  close() {
    this.dialogOpened = false;
  }

  /**
   * Clears the form. Only reachable through the Close button, because the
   * dialog opts out of Escape and outside-click dismissal.
   *
   * That opt-out is deliberate and restores what this dialog had as a
   * `<paper-dialog modal>`: `modal` implies `noCancelOnOutsideClick` and
   * `noCancelOnEscKey` (paper-dialog-behavior.js), so neither gesture could
   * close it. Every other converted dialog dismisses freely, but this one holds
   * work that exists nowhere else until Save — AD users added to `Privileges`
   * and the permission bits ticked on them — and this method throws it away.
   * Deferring the reset would not help: `open()` refetches from the API and
   * overwrites `Privileges`, so a stray click would still lose the edits.
   */
  private resetDialogState() {
    this.Privileges = [];
    this.ErrorMessage = '';
    this.setTextField('search-criteria', '');
    const searchResult = this.shadowRoot?.getElementById(
      'searchResults'
    ) as ComboBox;
    if (searchResult) searchResult.selectedItem = undefined;
  }
}
