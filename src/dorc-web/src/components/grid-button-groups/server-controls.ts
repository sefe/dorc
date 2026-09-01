import { confirmPrompt } from '../confirm-prompt';
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
import '@vaadin/tooltip';

@customElement('server-controls')
export class ServerControls extends LitElement {
  @property({ type: Object }) serverDetails: ServerApiModel | undefined;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean })
  envSet = false;

  @property({ type: Boolean }) readonly = true;

  static get styles() {
    return css`
      :host {
        display: inline-flex;
        align-items: center;
        flex-wrap: nowrap;
        gap: var(--lumo-space-xs);
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    const unlinkStyles = {
      color: this.readonly
        ? 'var(--dorc-text-secondary)'
        : 'var(--dorc-error-color)'
    };
    const editStyles = {
      color: this.readonly
        ? 'var(--dorc-text-secondary)'
        : 'var(--dorc-link-color)'
    };
    return html`
      <vaadin-button
        aria-label="Edit Server Details"
        theme="icon"
        @click="${this.editServer}"
        ?disabled="${this.readonly}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="Edit Server Details"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="lumo:edit"
          style=${styleMap(editStyles)}
        ></vaadin-icon>
      </vaadin-button>

      <vaadin-button
        title="Edit Tags"
        theme="icon"
        @click="${this.manage}"
        ?disabled="${this.readonly}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="Edit Application Tags"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="vaadin:tags"
          style=${styleMap(editStyles)}
        ></vaadin-icon>
      </vaadin-button>

      <vaadin-button
        aria-label="Manage Daemons"
        theme="icon"
        @click="${this.manageDaemons}"
        ?disabled="${this.readonly}"
      >
        <vaadin-tooltip slot="tooltip" text="Manage Daemons"></vaadin-tooltip>
        <vaadin-icon
          icon="vaadin:cog"
          style=${styleMap(editStyles)}
        ></vaadin-icon>
      </vaadin-button>

      ${
        this.envSet
          ? html`<vaadin-button
              aria-label="Detach server"
              theme="icon"
              @click="${this.detachServer}"
              ?disabled="${this.readonly}"
            >
              <vaadin-tooltip
                slot="tooltip"
                text="Detach server"
              ></vaadin-tooltip>
              <vaadin-icon
                icon="vaadin:unlink"
                style=${styleMap(unlinkStyles)}
              ></vaadin-icon>
            </vaadin-button>`
          : html``
      }
      ${
        !this.envSet
          ? html`<vaadin-button
              aria-label="Delete server"
              theme="icon"
              @click="${this.deleteServer}"
              ?disabled="${this.readonly}"
            >
              <vaadin-tooltip
                slot="tooltip"
                text="Delete server"
              ></vaadin-tooltip>
              <vaadin-icon
                icon="icons:delete"
                style=${styleMap(unlinkStyles)}
              ></vaadin-icon>
            </vaadin-button>`
          : html``
      }
    `;
  }

  async detachServer() {
    // Snapshot before awaiting: this control sits in a recycled grid cell, so
    // `this.serverDetails` can belong to a different row by the time the user
    // answers.
    const server = this.serverDetails;
    const envId = this.envId;
    const answer = await confirmPrompt(`Detach server ${server?.Name}?`);
    if (answer && server?.ServerId) {
      const api = new RefDataEnvironmentsDetailsApi();
      api
        .refDataEnvironmentsDetailsPut({
          componentId: server.ServerId,
          component: 'server',
          action: 'detach',
          envId
        })
        .subscribe(() => {
          this.fireServerDetachedEvent();
        });
    }
  }

  async deleteServer() {
    // Snapshot before awaiting: this control sits in a recycled grid cell, so
    // `this.serverDetails` can belong to a different row by the time the user
    // answers — and with the daemon-detach path there are now two prompts and a
    // network round trip between the question and the delete.
    const server = this.serverDetails;
    const answer = await confirmPrompt(`Delete server ${server?.Name}?`);
    if (answer && server?.ServerId) {
      this.performDeleteServer(server, false);
    }
  }

  private performDeleteServer(server: ServerApiModel, confirmed: boolean) {
    const api = new RefDataServersApi();
    api
      .refDataServersDelete({
        serverId: server.ServerId as number,
        confirmed
      })
      .subscribe({
        next: async (result: ApiBoolResult) => {
          if (result.Result === true) {
            // The snapshot, not a fresh read: the cell can be recycled during
            // the network round trip, and the delete itself refreshes the grid.
            const event = new CustomEvent('server-deleted', {
              composed: true,
              bubbles: true,
              detail: {
                server
              }
            });
            this.dispatchEvent(event);
            return;
          }

          const isDaemonWarning =
            !confirmed && result.RequiresConfirmation === true;

          if (isDaemonWarning) {
            const confirmDetach = await confirmPrompt(
              `${result.Message}\n\nDo you want to detach the daemon(s) and delete the server anyway?`
            );
            if (confirmDetach) {
              this.performDeleteServer(server, true);
            }
            return;
          }

          const notification = new ErrorNotification();
          notification.setAttribute('errorMessage', result.Message ?? '');
          this.shadowRoot?.appendChild(notification);
          notification.open();
          console.error(result.Message);
        },
        error: err => console.error(err)
      });
  }

  manage() {
    this.fireManageServerTags();
  }

  manageDaemons() {
    const event = new CustomEvent('map-daemons', {
      bubbles: true,
      composed: true,
      detail: {
        server: this.serverDetails
      }
    });
    this.dispatchEvent(event);
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
