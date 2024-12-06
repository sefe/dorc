import { css, LitElement } from 'lit';
import '@vaadin/icons';
import '@vaadin/icon';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { urlForName } from '../../router';
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
        width: var(--lumo-icon-size-m);
        height: var(--lumo-icon-size-m);
      }
    `;
  }

  render() {
    return html` <div style="margin-left: 20px; width: 270px">
      <a
        style="float:left"
        href="${urlForName('monitor-result', {
          id: String(this.requestStatus?.Id)
        })}"
      >
        <vaadin-vertical-layout style="align-items: start;" theme="compact">
          <vaadin-horizontal-layout
            style="line-height: var(--lumo-line-height-m);"
          >
            <vaadin-icon icon="vaadin:clipboard-pulse"></vaadin-icon>
            <span
              >${this.requestStatus?.Id}
              ${this.requestStatus?.EnvironmentName}</span
            >
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
