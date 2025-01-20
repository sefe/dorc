import '@vaadin/button';
import '@vaadin/text-field';
import { css, LitElement, PropertyValueMap } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Notification } from '@vaadin/notification';
import { ApiBoolResult, EnvironmentApiModel, RefDataEnvironmentsDetailsApi } from '../apis/dorc-api';

@customElement('add-env-tenant')
export class AddEnvTenant extends LitElement {
  @property({ type: Object }) parentEnvironment: EnvironmentApiModel | undefined;
  @property({ type: Array }) possibleTenants: Array<EnvironmentApiModel> | undefined;
  @state() selectedEnvironmentId: number | undefined;
  @state() envsLoading: boolean = false;

  static get styles() {
    return css`
      :host {
        display: block;
        padding: 16px;
      }
      .form {
        display: flex;
        align-items: center;
        gap: 8px;
      }
      .combobox-wrapper {
        display: flex;
        flex-direction: column;
        flex-grow: 1;
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
    `;
  }

  render() {
    return html`
      <div class="form">
        <div class="combobox-wrapper">
          <vaadin-combo-box
            id="environments"
            @value-changed="${this._environmentValueChanged}"
            .items="${this.possibleTenants}"
            placeholder="Select Environment"
            label="Environment"
            item-label-path="EnvironmentName"
            item-value-path="EnvironmentId"
            style="flex-grow: 1;"
            clear-button-visible
          ></vaadin-combo-box>
          <div class="small-loader" ?hidden="${!this.envsLoading}"></div>
          <vaadin-button @click="${this._addTenantEnvironment}" ?disabled="${!this.selectedEnvironmentId}">Attach As Tenant</vaadin-button>
        </div>
      </div>
    `;
  }

  private _addTenantEnvironment() {
    if (!this.selectedEnvironmentId) {
      Notification.show('Please select an environment from the list.', { position: 'bottom-start', duration: 3000 });
      return;
    }
    const envId = this.selectedEnvironmentId;
    const api = new RefDataEnvironmentsDetailsApi();
    api.refDataEnvironmentsDetailsSetParentForEnvironmentPut({
      childEnvId: this.selectedEnvironmentId,
      parentEnvId: this.parentEnvironment?.EnvironmentId
    })
      .subscribe({
        next: (data: ApiBoolResult) => {
          if (data.Result) {
            this.dispatchEvent(new CustomEvent('request-environment-update', {
              bubbles: true,
              composed: true
            }));
            this._fetchPossibleTenants();
            Notification.show(`Tenant environment with ID ${envId} added successfully.`, {
              theme: 'success',
              position: 'bottom-start',
              duration: 3000
            });
          }
          else {
            this.onError(`Set parent for environment with ID ${envId} has failed: ${data.Message}`);
          }
        },
        error: (err: string) => {
          this.onError(`Unable to set parent for environment: ${err}`);
          this.envsLoading = false
        }
      });

    // Clear the selection in the combobox
    const comboBox = this.shadowRoot?.getElementById('environments') as any;
    comboBox.value = null;
    this.selectedEnvironmentId = undefined;
  }

  private _environmentValueChanged(event: CustomEvent) {
    this.selectedEnvironmentId = event.detail.value;
  }

  connectedCallback() {
    super.connectedCallback();
    this._fetchPossibleTenants();
  }

  updated(changedProperties: PropertyValueMap<any>) {
    if (changedProperties.has('parentEnvironment')) {
      this._fetchPossibleTenants();
    }
  }

  private _fetchPossibleTenants() {
    this.envsLoading = true;
    const api = new RefDataEnvironmentsDetailsApi();
    api.refDataEnvironmentsDetailsGetPossibleEnvironmentChildrenGet({ id: this.parentEnvironment?.EnvironmentId })
      .subscribe({
        next: (data: Array<EnvironmentApiModel>) => {
          this.possibleTenants = data;
          this.envsLoading = false
        },
        error: (err: string) => {
          this.onError(`Unable to fetch possible tenants: ${err}`);
          this.envsLoading = false
        }
      });
  }

  private onError(message: string) {
    const event = new CustomEvent('error-alert', {
      detail: {
        description: message
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);

    console.error(message);
  }
}