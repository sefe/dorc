import { css, LitElement } from 'lit';
import '@vaadin/text-field';
import { TextField } from '@vaadin/text-field';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import type { PermissionDto } from '../apis/dorc-api';
import { Notification } from '@vaadin/notification';
import { RefDataPermissionApi } from '../apis/dorc-api';

@customElement('add-permission')
export class AddPermission extends LitElement {
  @property() private displayName = '';

  @property({ type: Boolean }) private displayNameValid = false;

  @property() private permissionName = '';

  @property({ type: Boolean }) private permissionNameValid = false;

  @property({ type: Boolean }) private valid = false;

  @property({ type: Object })
  private permission: PermissionDto = this.getEmptyPermission();

  @property() private overlayMessage: any;
  @property() private errorMessage: any;

  static get styles() {
    return css`
      .block {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 500px;
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
      <div style="width:50%;">
        <vaadin-vertical-layout>
          <vaadin-text-field
            class="block"
            id="display-name"
            label="Display Name"
            required
            auto-validate
            @input="${this._displayNameValueChanged}"
            .value="${this.displayName}"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            id="permission-name"
            label="Permission Name"
            required
            auto-validate
            @input="${this._daemonNameValueChanged}"
            .value="${this.permissionName}"
          ></vaadin-text-field>
        </vaadin-vertical-layout>
        <div>
          <vaadin-button @click="${this.reset}">Clear</vaadin-button>
          <vaadin-button .disabled="${!this.valid}" @click="${this._submit}"
            >Save</vaadin-button
          >
        </div>
      </div>
      <div>
        <span style="color: darkred">${this.overlayMessage}</span>
      </div>
      <div style="width: 400px">
        <span style="color: darkred">${this.errorMessage}</span>
      </div>
    `;
  }

  _displayNameValueChanged(data: any) {
    this.displayName = data.currentTarget.value;
    this.displayNameValid = this.displayName.length > 0;
    this.validate();
  }

  _daemonNameValueChanged(data: any) {
    this.permissionName = data.currentTarget.value;
    this.permissionNameValid = this.permissionName.length > 0;
    this.validate();
  }

  validate() {
    if (this.permission !== undefined) {
      if (this.permissionNameValid && this.displayNameValid) {
        this.valid = true;
      } else {
        this.valid = false;
      }
    }
  }

  _submit() {
    const api = new RefDataPermissionApi();

    this.permission.DisplayName = this.displayName.trim();
    this.permission.PermissionName = this.permissionName.trim();

    api.refDataPermissionPost({ permissionDto: this.permission }).subscribe({
      next: () => {
        this._addPermission(this.permission);
      },
      error: (err: any) => {
        this.overlayMessage = 'Error creating permission!';
        if (err?.response)
          this.errorMessage =  err.response;
        console.error(err);
      },
      complete: () => {
        console.log('done adding permission');
        this.reset();
        Notification.show(`Permission added successfully`, {
                      theme: 'success',
                      position: 'bottom-start',
                      duration: 3000
                    });
      }
    });
  }

  _addPermission(data: PermissionDto) {
    if (data.Id !== 0) {
      const event = new CustomEvent('permission-created', {
        detail: {
          daemon: this.permission
        },
        bubbles: true,
        composed: true
      });
      this.dispatchEvent(event);
    } else {
      this.overlayMessage = 'Error adding permission!';
    }
  }

  clearTextField(name: string) {
    const field = this.shadowRoot?.getElementById(name) as TextField;
    if (field) {
      field.value = '';
    }
  }

  reset() {
    this.clearTextField('permission-name');
    this.clearTextField('display-name');

    this.permission = this.getEmptyPermission();
    this.displayNameValid = false;
    this.permissionNameValid = false;

    this.valid = false;
    this.overlayMessage = '';
    this.errorMessage = '';
  }

  getEmptyPermission(): PermissionDto {
    const perm: PermissionDto = {
      DisplayName: '',
      PermissionName: ''
    };
    return perm;
  }
}
