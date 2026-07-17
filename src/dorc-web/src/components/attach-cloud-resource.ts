import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/combo-box';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import { CloudResourceApiModel, RefDataCloudResourcesApi } from '../apis/dorc-api';

/**
 * Attach an existing cloud resource to the environment. Fires `cloud-resource-attached`.
 */
@customElement('attach-cloud-resource')
export class AttachCloudResource extends LitElement {
  @property({ type: Number }) envId = 0;

  @property({ type: Array }) private cloudResources: CloudResourceApiModel[] = [];

  private selected: CloudResourceApiModel | undefined;

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
    new RefDataCloudResourcesApi().refDataCloudResourcesGet().subscribe({
      next: (data: CloudResourceApiModel[]) => {
        this.cloudResources = data;
      },
      error: (err: any) => console.error(err)
    });
  }

  render() {
    return html`
      <vaadin-combo-box
        label="Cloud Resource"
        item-label-path="Name"
        .items="${this.cloudResources}"
        @value-changed="${(e: CustomEvent) => {
          this.selected = this.cloudResources.find(c => c.Name === e.detail.value);
        }}"
      ></vaadin-combo-box>
      <vaadin-button theme="primary" @click="${this.attach}">Attach</vaadin-button>
    `;
  }

  private attach() {
    if (!this.selected?.Id) {
      Notification.show('Select a cloud resource to attach', {
        theme: 'error',
        position: 'bottom-start',
        duration: 5000
      });
      return;
    }

    new RefDataCloudResourcesApi()
      .refDataCloudResourcesIdEnvironmentsEnvIdPut({
        id: this.selected.Id,
        envId: this.envId
      })
      .subscribe({
        next: () => {
          this.dispatchEvent(
            new CustomEvent('cloud-resource-attached', {
              detail: { message: `Cloud resource ${this.selected?.Name} attached` },
              bubbles: true,
              composed: true
            })
          );
        },
        error: (err: any) => {
          Notification.show(
            `Failed to attach cloud resource: ${err.response ?? err.message ?? err}`,
            { theme: 'error', position: 'bottom-start', duration: 5000 }
          );
        }
      });
  }
}
