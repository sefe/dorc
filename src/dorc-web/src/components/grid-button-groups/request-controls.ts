import { css, LitElement } from 'lit';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../dorc-icon.js';
import { RequestApi } from '../../apis/dorc-api';

@customElement('request-controls')
export class RequestControls extends LitElement {
  @property({ type: Number })
  requestId = 0;

  @property({ type: Boolean })
  cancelable = false;

  @property({ type: Boolean })
  canRestart = false;

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
      .table-button {
        width: 36px;
        height: 100%;
      }
      vaadin-grid#grid {
        overflow: hidden;
      }
      vaadin-text-field {
        padding: 0px;
        margin: 0px;
      }
      vaadin-grid-cell-content {
        padding-top: 0px;
        padding-bottom: 0px;
        margin: 0px;
      }
      vaadin-button:disabled,
      vaadin-button[disabled] {
        background-color: #dde2e8;
      }
    `;
  }

  render() {
    return html`
      <table style="height: 36px">
        <tr>
          <td class="table-button">
            <vaadin-button
              title="Cancel Request"
              theme="icon small"
              @click="${this.cancel}"
              ?disabled="${!this.cancelable}"
            >
              <dorc-icon
                icon="stop"
                color="${this.cancelable ? 'danger' : 'neutral'}"
              ></dorc-icon>
            </vaadin-button>
          </td>
          <td class="table-button">
            <vaadin-button
              title="Restart Request"
              theme="icon small"
              @click="${this.restart}"
              ?disabled="${!this.canRestart}"
            >
              <dorc-icon
                icon="repeat"
                color="${this.canRestart ? 'primary' : 'neutral'}"
              ></dorc-icon>
            </vaadin-button>
          </td>
        </tr>
      </table>
    `;
  }

  restart() {
    const answer = confirm(
      `Are you sure you want to restart the job with ID ${this.requestId} ?`
    );

    if (answer) {
      const api = new RequestApi();
      api.requestRestartPost({ requestId: this.requestId }).subscribe(() => {
        const event = new CustomEvent('request-restarted', {
          detail: {
            requestId: this.requestId,
            message: 'Requested deploy has been restarted'
          },
          bubbles: true,
          composed: true
        });
        this.dispatchEvent(event);
      });
    }
  }

  cancel() {
    const answer = confirm(
      `Are you sure you want to cancel the job with ID ${this.requestId} ?`
    );

    if (answer) {
      const api = new RequestApi();
      api.requestCancelPut({ requestId: this.requestId }).subscribe(() => {
        const event = new CustomEvent('request-cancelled', {
          detail: {
            requestId: this.requestId,
            message: 'Requested deploy has been canceled'
          },
          bubbles: true,
          composed: true
        });
        this.dispatchEvent(event);
      });
    }
  }
}
