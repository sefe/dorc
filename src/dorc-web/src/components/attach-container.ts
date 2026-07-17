import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/combo-box';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import { ContainerApiModel, RefDataContainersApi } from '../apis/dorc-api';

/**
 * Attach an existing container to the environment. Fires `container-attached`.
 */
@customElement('attach-container')
export class AttachContainer extends LitElement {
  @property({ type: Number }) envId = 0;

  @property({ type: Array }) private containers: ContainerApiModel[] = [];

  private selected: ContainerApiModel | undefined;

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
    new RefDataContainersApi().refDataContainersGet().subscribe({
      next: (data: ContainerApiModel[]) => {
        this.containers = data;
      },
      error: (err: any) => console.error(err)
    });
  }

  render() {
    return html`
      <vaadin-combo-box
        label="Container"
        item-label-path="Name"
        .items="${this.containers}"
        @value-changed="${(e: CustomEvent) => {
          this.selected = this.containers.find(c => c.Name === e.detail.value);
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
      Notification.show('Select a container to attach', {
        theme: 'error',
        position: 'bottom-start',
        duration: 5000
      });
      return;
    }

    new RefDataContainersApi()
      .refDataContainersIdEnvironmentsEnvIdPut({
        id: this.selected.Id,
        envId: this.envId
      })
      .subscribe({
        next: () => {
          this.dispatchEvent(
            new CustomEvent('container-attached', {
              detail: { message: `Container ${this.selected?.Name} attached` },
              bubbles: true,
              composed: true
            })
          );
        },
        error: (err: any) => {
          Notification.show(
            `Failed to attach container: ${err.response ?? err.message ?? err}`,
            { theme: 'error', position: 'bottom-start', duration: 5000 }
          );
        }
      });
  }
}
