import { comboBoxRenderer } from '@vaadin/combo-box/lit';
import { confirmPrompt } from './confirm-prompt';
import { css, LitElement } from 'lit';
import '@vaadin/checkbox';
import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/dialog';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import {
  ApiBoolResult,
  ResetAppPasswordApi,
  UserApiModel
} from '../apis/dorc-api';
import { SuccessNotification } from './notifications/success-notification';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

@customElement('reset-app-password-behalf')
export class ResetAppPasswordBehalf extends LitElement {
  @state() private dialogOpened = false;

  @property({ type: Array }) appUsers: Array<UserApiModel> = [];

  @property({ type: String }) selectedUser = '';

  @property({ type: Boolean }) resettingAppPassword = false;

  @property({ type: String }) envFilter: string | undefined;

  @property({ type: String }) environmentName: string | undefined;
  @property({ type: String }) serverName: string | undefined;
  @property({ type: String }) databaseName: string | undefined;

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
      vaadin-dialog::part(overlay) {
        top: 16px;
        overflow: auto;
        max-width: calc(100vw - 32px);
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
      <vaadin-dialog
        id="reset-app-password-behalf-dialog"
        header-title="Reset Application Password"
        draggable
        .opened="${this.dialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.dialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderResetContent, [
          this.appUsers,
          this.serverName,
          this.databaseName,
          this.resettingAppPassword,
          this.selectedUser,
          this.envFilter,
          this.environmentName
        ])}
        ${dialogFooterRenderer(this.renderResetFooter, [])}
      ></vaadin-dialog>
    `;
  }

  async resetAppPassword() {
    const user = this.appUsers.find(u => u.LanId === this.selectedUser);
    if (user === undefined) return;

    // Snapshot the environment inputs alongside the user: all three name the
    // account being reset, and the host can reassign them while the
    // confirmation is open.
    const envFilter = this.envFilter ?? '';
    const environmentName = this.environmentName ?? '';
    const answer = await confirmPrompt(
      `Are you sure you want to reset the SQL account password for ${
        user.DisplayName
      }?`
    );
    if (answer) {
      this.resettingAppPassword = true;
      const api = new ResetAppPasswordApi(dorcApiConfiguration);
      api
        .resetAppPasswordForUserPut({
          envFilter,
          envName: environmentName,
          username: user.LanId ?? ''
        })
        .subscribe({
          next: (result: ApiBoolResult) => {
            if (result.Result) {
              const appName = envFilter;
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
      detail: {
        description: 'Failed to reset the SQL account password',
        result
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  appUserValueChanged(data: CustomEvent<any>) {
    this.selectedUser = data.detail.value;
  }

  appUsersRenderer(item: UserApiModel) {
    return html`<vaadin-vertical-layout>
      <div style="line-height: var(--lumo-line-height-m);">
        ${item.DisplayName ?? ''}
      </div>
      <div
        style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
      >
        ${item.LanId ?? ''}
      </div>
    </vaadin-vertical-layout>`;
  }

  /**
   * The combo-box renderer stays a plain `.renderer` binding for now — it is a
   * grid/combo-box renderer, which belongs to the separate F-4 migration, not
   * this dialog conversion. It is listed in that audit.
   */
  private renderResetContent = () => html`
    <div>
      <vaadin-combo-box
        id="app-users"
        label="Select User"
        item-value-path="LanId"
        item-label-path="DisplayName"
        .items="${this.appUsers}"
        ${comboBoxRenderer(this.appUsersRenderer, [])}
        @value-changed="${this.appUserValueChanged}"
        style="width: 100%"
      ></vaadin-combo-box>

      <div style="margin-right: 30px">
        <vaadin-button
          @click="${this.resetAppPassword}"
          ?disabled="${this.serverName === undefined && this.databaseName === undefined}"
          >Reset Password</vaadin-button
        >
        ${
          this.resettingAppPassword
            ? html` <div class="small-loader"></div> `
            : html``
        }
      </div>

      <div class="info-section">
        <div class="action-description">
          This will reset the selected user's SQL account password for the next
          server and database:
        </div>
        <br />
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
  `;

  private renderResetFooter = () => html`
    <vaadin-button @click="${() => (this.dialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  public open() {
    this.dialogOpened = true;
  }

  public close() {
    this.dialogOpened = false;
  }
}
