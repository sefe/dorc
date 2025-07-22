import { css, LitElement } from 'lit';
import '@vaadin/icons';
import { customElement, property } from 'lit/decorators.js';
import '../dorc-icon.js';
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
            <dorc-icon icon="clipboard"></dorc-icon>
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
      <dorc-icon icon="close-small" color="lightblue"></dorc-icon>
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
