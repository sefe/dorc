import { css, LitElement } from 'lit';
import '@vaadin/icons';
import '@vaadin/icon';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { urlForName } from '../../router/router';
import '@vaadin/horizontal-layout';
import { DeploymentRequestApiModel } from '../../apis/dorc-api';

@customElement('monitor-result-tab')
export class MonitorResultTab extends LitElement {
  @property({ type: Object }) public requestStatus:
    | DeploymentRequestApiModel
    | undefined;

  static get styles() {
    return css`
      a {
        color: inherit; /* blue colors for links too */
        text-decoration: inherit; /* no underline */
        display: block;
        width: 100%;
      }
      vaadin-icon {
        width: var(--lumo-icon-size-s);
        height: var(--lumo-icon-size-s);
        font-size: var(--lumo-font-size-s);
      }
      .restart-badge {
        background-color: #ff6b35;
        color: white;
        padding: 2px 6px;
        border-radius: 3px;
        font-size: var(--lumo-font-size-xs);
        margin-left: 4px;
        font-weight: 500;
      }
    `;
  }

  render() {
    // Navigate to original request if this is a restart
    const targetRequestId = this.requestStatus?.ParentRequestId ?? this.requestStatus?.Id;
    const isRestart = !!this.requestStatus?.ParentRequestId;
    
    return html` <div style="margin-left: 20px; width: 270px">
      <a
        style="float:left"
        href="${urlForName('monitor-result', {
          id: String(targetRequestId)
        })}"
      >
        <vaadin-vertical-layout style="align-items: start;" theme="compact">
          <vaadin-horizontal-layout
            style="line-height: var(--lumo-line-height-m);"
          >
            <vaadin-icon
              icon="vaadin:clipboard-pulse"
              theme="small"
            ></vaadin-icon>
            <span>
              ${this.requestStatus?.Id}
              ${this.requestStatus?.EnvironmentName}
              ${isRestart ? html`<span class="restart-badge">↻ RESTART</span>` : ''}
            </span>
          </vaadin-horizontal-layout>
          <div
            title="${this.requestStatus?.BuildNumber ?? ''}"
            style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
          >
            ${this.requestStatus?.BuildNumber}
          </div>
        </vaadin-vertical-layout>
      </a>
      <vaadin-icon
        style="color: lightblue; float: right;  position: absolute; right: 5px; top: 5px;"
        icon="vaadin:close-small"
        theme="small"
        @click="${this.removeMonitorResult}"
      ></vaadin-icon>
    </div>`;
  }

  removeMonitorResult() {
    const event = new CustomEvent('close-monitor-result', {
      detail: {
        request: this.requestStatus
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}
