import { css, LitElement, render } from 'lit';
import '@vaadin/checkbox';
import '@vaadin/button';
import '@vaadin/combo-box';
import '@polymer/paper-dialog';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PaperDialogElement } from '@polymer/paper-dialog';
import { ComboBox, ComboBoxItemModel } from '@vaadin/combo-box';
import {
  ApiBoolResult,
  ResetAppPasswordApi,
  UserApiModel
} from '../apis/dorc-api';
import { SuccessNotification } from './notifications/success-notification';

@customElement('reset-app-password-behalf')
export class ResetAppPasswordBehalf extends LitElement {
  @property({ type: Array }) appUsers: Array<UserApiModel> = [];

  @property({ type: String }) private selectedUser = '';

  @property({ type: Boolean }) private resettingAppPassword = false;

  @property({ type: String }) protected envFilter: string | undefined;

  @property({ type: String }) protected environmentName: string | undefined;
  @property({ type: String }) protected serverName: string | undefined;
  @property({ type: String }) protected databaseName: string | undefined;

  static get styles() {
    return css`
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
      paper-dialog.size-position {
        position: center;
        top: 16px;
        overflow: auto;
        padding: 10px;
      }
      .info-section {
        background: #f7f7f7;
        padding: 12px 16px;
        border-radius: 6px;
        margin-bottom: 16px;
        margin-top: 16px;
        width: 300px;
      }
      .info-row {
        display: flex;
        justify-content: space-between;
        margin-bottom: 4px;
      }
      .info-label {
        font-weight: 500;
        color: #555;
      }
      .info-value {
        font-weight: 400;
        color: #222;
      }
    `;
  }

  render() {
    return html`
      <paper-dialog
        class="size-position"
        id="reset-app-password-behalf-dialog"
        allow-click-through
        modal
      >
        <div>
          <vaadin-combo-box
            id="app-users"
            label="Select User"
            item-value-path="LanId"
            item-label-path="DisplayName"
            .items="${this.appUsers}"
            .renderer="${this.appUsersRenderer}"
            @value-changed="${this.appUserValueChanged}"
            style="width: 100%"
          ></vaadin-combo-box>

          <div style="margin-right: 30px">
            <vaadin-button 
              @click="${this.resetAppPassword}" 
              ?disabled="${this.serverName === undefined && this.databaseName === undefined}"
              >Reset Password</vaadin-button
            >
            ${this.resettingAppPassword
              ? html` <div class="small-loader"></div> `
              : html``}
          </div>

          <div class="info-section">
            <div class="action-description">
              This will reset the selected user's SQL account password for the next server and database:
            </div>
            <br/>
            <div class="info-row">
              <span class="info-label">Server:</span>
              <span class="info-value">${this.serverName}</span>
            </div>
            <div class="info-row">
              <span class="info-label">Database:</span>
              <span class="info-value">${this.databaseName}</span>
            </div>
          </div>
        </div>
        <div style="display: flex; justify-content: flex-end">
          <vaadin-button dialog-confirm>Close</vaadin-button>
        </div>
      </paper-dialog>
    `;
  }

  resetAppPassword() {
    const user = this.appUsers.find(u => u.LanId === this.selectedUser);
    if (user === undefined) return;

    const answer = confirm(
      `Are you sure you want to reset the SQL account password for ${
        user.DisplayName
      }?`
    );
    if (answer) {
      this.resettingAppPassword = true;
      const api = new ResetAppPasswordApi();
      api
        .resetAppPasswordForUserPut({
          envFilter: this.envFilter ?? '',
          envName: this.environmentName ?? '',
          username: user.LanId ?? ''
        })
        .subscribe({
          next: (result: ApiBoolResult) => {
            if (result.Result) {
              const appName = this.envFilter ?? '';
              const message = `Password successfully reset, it is now set as the same as your ${
                appName
              } login name, you will need to login without encryption the first time.`;
              const notification = new SuccessNotification();
              notification.setAttribute('successMessage', message);
              this.shadowRoot?.appendChild(notification);
              notification.open();
            } else {
              this.errorAlert(result);
            }
            this.resettingAppPassword = false;
          },
          error: (err: any) => {
            this.errorAlert(err.response);
            this.resettingAppPassword = false;
          }
        });
    }
  }

  errorAlert(result: any) {
    const event = new CustomEvent('error-alert', {
      detail: { description: 'Failed to reset the SQL account password', result },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  appUserValueChanged(data: CustomEvent<any>) {
    this.selectedUser = data.detail.value;
  }

  appUsersRenderer(
    root: HTMLElement,
    _comboBox: ComboBox,
    model: ComboBoxItemModel<UserApiModel>
  ) {
    render(
      html`<vaadin-vertical-layout>
        <div style="line-height: var(--lumo-line-height-m);">
          ${model.item.DisplayName ?? ''}
        </div>
        <div
          style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
        >
          ${model.item.LanId ?? ''}
        </div>
      </vaadin-vertical-layout>`,
      root
    );
  }

  public open() {
    const dialog = this.shadowRoot?.getElementById(
      'reset-app-password-behalf-dialog'
    ) as PaperDialogElement;

    dialog.open();
  }

  public close() {
    const dialog = this.shadowRoot?.getElementById(
      'reset-app-password-behalf-dialog'
    ) as PaperDialogElement;
    dialog.close();
  }
}
