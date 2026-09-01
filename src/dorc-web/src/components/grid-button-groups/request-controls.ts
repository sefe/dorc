import { confirmPrompt } from '../confirm-prompt';
import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../../icons/av-icons.js';
import { styleMap } from 'lit/directives/style-map.js';
import { RequestApi } from '../../apis/dorc-api';
import { ajax } from 'rxjs/ajax';
import { appConfig } from '../../app-config';
import { oauthServiceContainer } from '../../services/Account/OAuthService';
import '@vaadin/tooltip';

@customElement('request-controls')
export class RequestControls extends LitElement {
  @property({ type: Number })
  requestId = 0;

  @property({ type: Boolean })
  cancelable = false;

  @property({ type: Boolean })
  canRestart = false;

  @property({ type: Boolean })
  canPause = false;

  @property({ type: Boolean })
  canResume = false;

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
        background-color: var(--dorc-border-color);
      }
    `;
  }

  render() {
    const cancelStyles = {
      color: this.cancelable
        ? 'var(--dorc-error-color)'
        : 'var(--dorc-text-secondary)'
    };
    const restartStyles = {
      color: this.canRestart
        ? 'var(--dorc-link-color)'
        : 'var(--dorc-text-secondary)'
    };
    const pauseStyles = {
      color: this.canPause
        ? 'var(--dorc-badge-text)'
        : 'var(--dorc-text-secondary)'
    };
    const resumeStyles = {
      color: this.canResume
        ? 'var(--dorc-success-text)'
        : 'var(--dorc-text-secondary)'
    };
    return html`
      <table style="height: 36px">
        <tr>
          <td class="table-button">
            <vaadin-button
              aria-label="Cancel Request"
              theme="icon small"
              @click="${this.cancel}"
              ?disabled="${!this.cancelable}"
            >
              <vaadin-tooltip
                slot="tooltip"
                text="Cancel Request"
              ></vaadin-tooltip>
              <vaadin-icon
                icon="av:stop"
                style=${styleMap(cancelStyles)}
              ></vaadin-icon>
            </vaadin-button>
          </td>
          <td class="table-button">
            <vaadin-button
              aria-label="Restart Request"
              theme="icon small"
              @click="${this.restart}"
              ?disabled="${!this.canRestart}"
            >
              <vaadin-tooltip
                slot="tooltip"
                text="Restart Request"
              ></vaadin-tooltip>
              <vaadin-icon
                icon="av:repeat"
                style=${styleMap(restartStyles)}
              ></vaadin-icon>
            </vaadin-button>
          </td>
          ${
            appConfig.pauseDeploymentEnabled
              ? html`
                  <td class="table-button">
                    <vaadin-button
                      aria-label="Pause Request"
                      theme="icon small"
                      @click="${this.pause}"
                      ?disabled="${!this.canPause}"
                    >
                      <vaadin-tooltip
                        slot="tooltip"
                        text="Pause Request"
                      ></vaadin-tooltip>
                      <vaadin-icon
                        icon="av:pause"
                        style=${styleMap(pauseStyles)}
                      ></vaadin-icon>
                    </vaadin-button>
                  </td>
                  <td class="table-button">
                    <vaadin-button
                      aria-label="Resume Request"
                      theme="icon small"
                      @click="${this.resume}"
                      ?disabled="${!this.canResume}"
                    >
                      <vaadin-tooltip
                        slot="tooltip"
                        text="Resume Request"
                      ></vaadin-tooltip>
                      <vaadin-icon
                        icon="av:play-arrow"
                        style=${styleMap(resumeStyles)}
                      ></vaadin-icon>
                    </vaadin-button>
                  </td>
                `
              : html``
          }
        </tr>
      </table>
    `;
  }

  async restart() {
    // Snapshot before awaiting. This control lives in a recycled grid cell
    // and the monitor grids auto-refresh, so `this.requestId` can be the
    // next row's by the time the user answers.
    const requestId = this.requestId;
    const answer = await confirmPrompt(
      `Are you sure you want to restart the job with ID ${requestId}?`
    );

    if (answer) {
      const api = new RequestApi();
      api.requestRestartPost({ requestId }).subscribe(() => {
        const event = new CustomEvent('request-restarted', {
          detail: {
            requestId,
            message: 'Requested deploy has been restarted'
          },
          bubbles: true,
          composed: true
        });
        this.dispatchEvent(event);
      });
    }
  }

  async cancel() {
    // Snapshot before awaiting. This control lives in a recycled grid cell
    // and the monitor grids auto-refresh, so `this.requestId` can be the
    // next row's by the time the user answers.
    const requestId = this.requestId;
    const answer = await confirmPrompt(
      `Are you sure you want to cancel the job with ID ${requestId}?`
    );

    if (answer) {
      const api = new RequestApi();
      api.requestCancelPut({ requestId }).subscribe(() => {
        const event = new CustomEvent('request-cancelled', {
          detail: {
            requestId,
            message: 'Requested deploy has been canceled'
          },
          bubbles: true,
          composed: true
        });
        this.dispatchEvent(event);
      });
    }
  }

  async pause() {
    // Snapshot before awaiting. This control lives in a recycled grid cell
    // and the monitor grids auto-refresh, so `this.requestId` can be the
    // next row's by the time the user answers.
    const requestId = this.requestId;
    const answer = await confirmPrompt(
      `Are you sure you want to pause the job with ID ${requestId}? This will block subsequent deployments to this environment.`
    );

    if (answer) {
      const headers: Record<string, string> = {
        'Content-Type': 'application/json'
      };
      const accessToken =
        oauthServiceContainer.service.signedInUser?.access_token;
      if (accessToken) {
        headers['Authorization'] = `Bearer ${accessToken}`;
      }

      ajax({
        url: `${appConfig.dorcApi}/Request/pause?requestId=${requestId}`,
        method: 'PUT',
        headers,
        withCredentials: true
      }).subscribe(() => {
        const event = new CustomEvent('request-paused', {
          detail: {
            requestId,
            message: 'Requested deploy has been paused'
          },
          bubbles: true,
          composed: true
        });
        this.dispatchEvent(event);
      });
    }
  }

  async resume() {
    // Snapshot before awaiting. This control lives in a recycled grid cell
    // and the monitor grids auto-refresh, so `this.requestId` can be the
    // next row's by the time the user answers.
    const requestId = this.requestId;
    const answer = await confirmPrompt(
      `Are you sure you want to resume the job with ID ${requestId}?`
    );

    if (answer) {
      const headers: Record<string, string> = {
        'Content-Type': 'application/json'
      };
      const accessToken =
        oauthServiceContainer.service.signedInUser?.access_token;
      if (accessToken) {
        headers['Authorization'] = `Bearer ${accessToken}`;
      }

      ajax({
        url: `${appConfig.dorcApi}/Request/resume?requestId=${requestId}`,
        method: 'PUT',
        headers,
        withCredentials: true
      }).subscribe(() => {
        const event = new CustomEvent('request-resumed', {
          detail: {
            requestId,
            message: 'Requested deploy has been resumed'
          },
          bubbles: true,
          composed: true
        });
        this.dispatchEvent(event);
      });
    }
  }
}
