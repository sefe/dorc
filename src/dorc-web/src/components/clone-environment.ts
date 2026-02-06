import '@polymer/paper-dialog';
import { PaperDialogElement } from '@polymer/paper-dialog';
import '@vaadin/button';
import '@vaadin/checkbox';
import '@vaadin/text-field';
import '@vaadin/vertical-layout';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import { css, LitElement } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Notification } from '@vaadin/notification';
import { EnvironmentApiModel } from '../apis/dorc-api';
import { BASE_PATH } from '../apis/dorc-api/runtime';

// Request interface for cloning environment
interface CloneEnvironmentRequest {
  SourceEnvironmentId: number;
  NewEnvironmentName: string;
  CopyPropertyValues?: boolean;
  CopyServerMappings?: boolean;
  CopyDatabaseMappings?: boolean;
  CopyProjectMappings?: boolean;
  CopyAccessControls?: boolean;
}

@customElement('clone-environment')
export class CloneEnvironment extends LitElement {
  @property({ type: Object }) sourceEnvironment: EnvironmentApiModel | undefined;

  @state() private newEnvironmentName = '';
  @state() private copyPropertyValues = false;
  @state() private copyServerMappings = false;
  @state() private copyDatabaseMappings = false;
  @state() private copyProjectMappings = false;
  @state() private copyAccessControls = false;
  @state() private isCloning = false;
  @state() private canClone = false;
  @state() private errorMessage = '';

  static get styles() {
    return css`
      paper-dialog.size-position {
        overflow: auto;
        width: 550px;
      }
      vaadin-text-field {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 400px;
        padding: 5px;
      }
      vaadin-checkbox {
        display: block;
        margin: 8px 0;
      }
      .small-loader {
        border: 2px solid #f3f3f3;
        border-top: 2px solid #3498db;
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
        display: inline-block;
      }
      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }
      .error-message {
        color: #ff3131;
        margin-top: 8px;
        padding: 0 10px;
      }
      .prod-badge {
        color: #d32f2f;
        font-size: var(--lumo-font-size-s);
        margin-left: 8px;
      }
    `;
  }

  render() {
    return html`
      <paper-dialog
        class="size-position"
        id="clone-environment-dialog"
        allow-click-through
        modal
      >
        <table>
          <tr>
            <td>
              <vaadin-icon
                icon="vaadin:copy-o"
                style="color: cornflowerblue"
              ></vaadin-icon>
            </td>
            <td>
              <h2>
                ${this.sourceEnvironment?.EnvironmentName ?? 'Not selected'}
                ${this.sourceEnvironment?.EnvironmentIsProd
                  ? html`<span class="prod-badge">(Production)</span>`
                  : ''}
              </h2>
            </td>
          </tr>
        </table>
        <div style="padding-left: 10px; padding-right: 10px;">
          <vaadin-text-field
            id="new-env-name"
            label="New Environment Name"
            required
            auto-validate
            .value=${this.newEnvironmentName}
            @value-changed=${this._nameValueChanged}
            placeholder="Enter name for the cloned environment"
            error-message="Name is required"
          ></vaadin-text-field>

          <div
            style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding: 12px; margin-top: 10px;"
          >
            <div style="font-weight: bold; margin-bottom: 8px;">
              Clone Options
            </div>

            <vaadin-checkbox
              .checked=${this.copyPropertyValues}
              @checked-changed=${(e: CustomEvent<{ value: boolean }>) => {
                this.copyPropertyValues = e.detail.value;
              }}
            >
              <label slot="label">Copy Property Values (Variables)</label>
            </vaadin-checkbox>

            <vaadin-checkbox
              .checked=${this.copyServerMappings}
              @checked-changed=${(e: CustomEvent<{ value: boolean }>) => {
                this.copyServerMappings = e.detail.value;
              }}
            >
              <label slot="label">Copy Server Mappings</label>
            </vaadin-checkbox>

            <vaadin-checkbox
              .checked=${this.copyDatabaseMappings}
              @checked-changed=${(e: CustomEvent<{ value: boolean }>) => {
                this.copyDatabaseMappings = e.detail.value;
              }}
            >
              <label slot="label">Copy Database Mappings</label>
            </vaadin-checkbox>

            <vaadin-checkbox
              .checked=${this.copyProjectMappings}
              @checked-changed=${(e: CustomEvent<{ value: boolean }>) => {
                this.copyProjectMappings = e.detail.value;
              }}
            >
              <label slot="label">Copy Project Mappings</label>
            </vaadin-checkbox>

            <vaadin-checkbox
              .checked=${this.copyAccessControls}
              @checked-changed=${(e: CustomEvent<{ value: boolean }>) => {
                this.copyAccessControls = e.detail.value;
              }}
            >
              <label slot="label">Copy Access Controls (Permissions)</label>
            </vaadin-checkbox>
          </div>

          ${this.errorMessage
            ? html`<div class="error-message">${this.errorMessage}</div>`
            : ''}
        </div>
        <div style="display: flex; justify-content: space-between; align-items: center; margin-top: 16px;">
          <div>
            <vaadin-button
              .disabled=${!this.canClone || this.isCloning}
              @click=${this._cloneEnvironment}
            >
              Save
            </vaadin-button>
            ${this.isCloning ? html`<div class="small-loader"></div>` : html``}
          </div>
          <vaadin-button dialog-confirm @click=${this.close}>Close</vaadin-button>
        </div>
      </paper-dialog>
    `;
  }

  private _nameValueChanged(e: CustomEvent<{ value: string }>) {
    this.newEnvironmentName = e.detail.value;
    this._validateForm();
  }

  private _validateForm() {
    this.canClone =
      !!this.sourceEnvironment &&
      !!this.newEnvironmentName &&
      this.newEnvironmentName.trim().length > 0 &&
      this.newEnvironmentName.trim() !== this.sourceEnvironment.EnvironmentName;

    if (
      this.newEnvironmentName.trim() ===
      this.sourceEnvironment?.EnvironmentName
    ) {
      this.errorMessage =
        'New environment name must be different from the source';
    } else {
      this.errorMessage = '';
    }
  }

  private _cloneEnvironment() {
    if (!this.sourceEnvironment?.EnvironmentId) {
      this.errorMessage = 'Source environment is not valid';
      return;
    }

    this.isCloning = true;
    this.errorMessage = '';

    const request: CloneEnvironmentRequest = {
      SourceEnvironmentId: this.sourceEnvironment.EnvironmentId,
      NewEnvironmentName: this.newEnvironmentName.trim(),
      CopyPropertyValues: this.copyPropertyValues,
      CopyServerMappings: this.copyServerMappings,
      CopyDatabaseMappings: this.copyDatabaseMappings,
      CopyProjectMappings: this.copyProjectMappings,
      CopyAccessControls: this.copyAccessControls
    };

    // Use direct fetch call until the API client is regenerated
    fetch(`${BASE_PATH}/RefDataEnvironments/Clone`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      credentials: 'include',
      body: JSON.stringify(request)
    })
      .then(async response => {
        if (!response.ok) {
          const errorText = await response.text();
          throw new Error(errorText || `HTTP ${response.status}`);
        }
        return response.json() as Promise<EnvironmentApiModel>;
      })
      .then((clonedEnv: EnvironmentApiModel) => {
        this.isCloning = false;

        Notification.show(
          `Environment '${clonedEnv.EnvironmentName}' created successfully!`,
          {
            theme: 'success',
            position: 'bottom-start',
            duration: 5000
          }
        );

        // Dispatch event to notify parent component
        const event = new CustomEvent('environment-cloned', {
          detail: { environment: clonedEnv },
          bubbles: true,
          composed: true
        });
        this.dispatchEvent(event);

        // Close and reset
        this.close();
      })
      .catch((err: Error) => {
        this.isCloning = false;
        console.error('Error cloning environment:', err);
        this.errorMessage = err.message || 'Failed to clone environment';
      });
  }

  private _resetForm() {
    this.newEnvironmentName = '';
    this.copyPropertyValues = false;
    this.copyServerMappings = false;
    this.copyDatabaseMappings = false;
    this.copyProjectMappings = false;
    this.copyAccessControls = false;
    this.canClone = false;
    this.errorMessage = '';
  }

  open(environment: EnvironmentApiModel) {
    this.sourceEnvironment = environment;
    this._resetForm();
    const dialog = this.shadowRoot?.getElementById(
      'clone-environment-dialog'
    ) as PaperDialogElement;
    dialog.open();
  }

  close() {
    const dialog = this.shadowRoot?.getElementById(
      'clone-environment-dialog'
    ) as PaperDialogElement;
    dialog.close();
    this._resetForm();
  }
}
