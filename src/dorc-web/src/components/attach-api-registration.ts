import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/combo-box';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import { ApiRegistrationApiModel, RefDataApiRegistrationsApi } from '../apis/dorc-api';

/**
 * Attach an existing API registration to the environment. Fires `api-registration-attached`.
 */
@customElement('attach-api-registration')
export class AttachApiRegistration extends LitElement {
  @property({ type: Number }) envId = 0;

  @property({ type: Array }) private apiRegistrations: ApiRegistrationApiModel[] = [];

  private selected: ApiRegistrationApiModel | undefined;

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
      }
      vaadin-combo-box {
        width: 320px;
      }
    `;
  }

  connectedCallback() {
    super.connectedCallback();
    new RefDataApiRegistrationsApi().refDataApiRegistrationsGet().subscribe({
      next: (data: ApiRegistrationApiModel[]) => {
        this.apiRegistrations = data;
      },
      error: (err: any) => console.error(err)
    });
  }

  render() {
    return html`
      <vaadin-combo-box
        label="API Registration"
        item-label-path="Name"
        .items="${this.apiRegistrations}"
        @value-changed="${(e: CustomEvent) => {
          this.selected = this.apiRegistrations.find(c => c.Name === e.detail.value);
        }}"
      ></vaadin-combo-box>
      <vaadin-button theme="primary" @click="${this.attach}">Attach</vaadin-button>
    `;
  }

  private attach() {
    if (this.envId <= 0) {
      Notification.show('Environment is still loading — try again', {
        theme: 'error',
        position: 'bottom-start',
        duration: 5000
      });
      return;
    }
    if (!this.selected?.Id) {
      Notification.show('Select an API registration to attach', {
        theme: 'error',
        position: 'bottom-start',
        duration: 5000
      });
      return;
    }

    new RefDataApiRegistrationsApi()
      .refDataApiRegistrationsIdEnvironmentsEnvIdPut({
        id: this.selected.Id,
        envId: this.envId
      })
      .subscribe({
        next: () => {
          this.dispatchEvent(
            new CustomEvent('api-registration-attached', {
              detail: { message: `API registration ${this.selected?.Name} attached` },
              bubbles: true,
              composed: true
            })
          );
        },
        error: (err: any) => {
          Notification.show(
            `Failed to attach API registration: ${err.response ?? err.message ?? err}`,
            { theme: 'error', position: 'bottom-start', duration: 5000 }
          );
        }
      });
  }
}
