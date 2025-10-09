import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { styleMap } from 'lit/directives/style-map.js';
import '../../icons/iron-icons.js';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { ErrorNotification } from '../notifications/error-notification';
import {
  ApiBoolResult,
  RefDataEnvironmentsDetailsApi,
  RefDataServersApi,
  ServerApiModel
} from '../../apis/dorc-api';

@customElement('server-controls')
export class ServerControls extends LitElement {
  @property({ type: Object }) serverDetails: ServerApiModel | undefined;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean })
  envSet = false;

  @property({ type: Boolean }) private readonly = true;

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    const unlinkStyles = {
      color: this.readonly ? 'grey' : '#FF3131'
    };
    const editStyles = {
      color: this.readonly ? 'grey' : 'cornflowerblue'
    };
    return html`
      <vaadin-button
        title="Edit Server Details"
        theme="icon"
        @click="${this.editServer}"
        ?disabled="${this.readonly}"
      >
        <vaadin-icon
          icon="lumo:edit"
          style=${styleMap(editStyles)}
        ></vaadin-icon>
      </vaadin-button>

      <vaadin-button
        title="Edit Application Tags"
        theme="icon"
        @click="${this.manage}"
        ?disabled="${this.readonly}"
      >
        <vaadin-icon
          icon="vaadin:tags"
          style=${styleMap(editStyles)}
        ></vaadin-icon>
      </vaadin-button>

      ${this.envSet
        ? html`<vaadin-button
            title="Detach server"
            theme="icon"
            @click="${this.detachServer}"
            ?disabled="${this.readonly}"
          >
            <vaadin-icon
              icon="vaadin:unlink"
              style=${styleMap(unlinkStyles)}
            ></vaadin-icon>
          </vaadin-button>`
        : html``}
      ${!this.envSet
        ? html`<vaadin-button
            title="Delete server"
            theme="icon"
            @click="${this.deleteServer}"
            ?disabled="${this.readonly}"
          >
            <vaadin-icon
              icon="icons:delete"
              style=${styleMap(unlinkStyles)}
            ></vaadin-icon>
          </vaadin-button>`
        : html``}
    `;
  }

  detachServer() {
    const answer = confirm(`Detach server ${this.serverDetails?.Name}?`);
    if (answer && this.serverDetails?.ServerId) {
      const api = new RefDataEnvironmentsDetailsApi();
      api
        .refDataEnvironmentsDetailsPut({
          componentId: this.serverDetails?.ServerId,
          component: 'server',
          action: 'detach',
          envId: this.envId
        })
        .subscribe(() => {
          this.fireServerDetachedEvent();
        });
    }
  }

  deleteServer() {
    const answer = confirm(`Delete server ${this.serverDetails?.Name}?`);
    if (answer && this.serverDetails?.ServerId) {
      const api = new RefDataServersApi();
      api
        .refDataServersDelete({
          serverId: this.serverDetails.ServerId
        })
        .subscribe({
          next: (result: ApiBoolResult) => {
            if (result.Result === true) {
              const server = this.serverDetails;
              const event = new CustomEvent('server-deleted', {
                composed: true,
                bubbles: true,
                detail: {
                  server
                }
              });
              this.dispatchEvent(event);
            } else {
              const notification = new ErrorNotification();
              notification.setAttribute('errorMessage', result.Message ?? '');
              this.shadowRoot?.appendChild(notification);
              notification.open();
              console.error(result.Message);
            }
          },
          error: err => console.error(err),
          complete: () =>
            console.log(`Deleted Server ${this.serverDetails?.Name}`)
        });
    }
  }

  manage() {
    this.fireManageServerTags();
  }

  editServer() {
    const event = new CustomEvent('edit-server', {
      bubbles: true,
      composed: true,
      detail: {
        server: this.serverDetails
      }
    });
    this.dispatchEvent(event);
  }

  private fireServerDetachedEvent() {
    const event = new CustomEvent('server-detached', {
      detail: {
        message: 'server detached successfully!'
      }
    });
    this.dispatchEvent(event);
  }

  private fireManageServerTags() {
    const event = new CustomEvent('manage-server-tags', {
      bubbles: true,
      composed: true,
      detail: {
        server: this.serverDetails
      }
    });
    this.dispatchEvent(event);
  }
}
