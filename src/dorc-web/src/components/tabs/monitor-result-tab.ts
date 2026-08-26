import { LitElement } from 'lit';
import '@vaadin/icons';
import '@vaadin/icon';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { urlForName } from '../../router/router';
import { DeploymentRequestApiModel } from '../../apis/dorc-api';

@customElement('monitor-result-tab')
export class MonitorResultTab extends LitElement {
  @property({ type: Object }) public requestStatus:
    | DeploymentRequestApiModel
    | undefined;

  /** Light DOM so `vaadin-tab._onKeyUp` can find the anchor — see env-detail-tab. */
  protected createRenderRoot() {
    return this;
  }

  render() {
    const id = this.requestStatus?.Id ?? '';
    const envName = this.requestStatus?.EnvironmentName ?? '';
    const build = this.requestStatus?.BuildNumber ?? '';
    const label = `${id} ${envName}`.trim();

    return html`
      <a
        class="shortcut-link shortcut-link--stacked"
        href="${urlForName('monitor-result', { id: String(id) })}"
        title="${label}${build ? ` — ${build}` : ''}"
      >
        <span class="shortcut-line">
          <vaadin-icon
            class="shortcut-icon"
            icon="vaadin:clipboard-pulse"
            theme="small"
          ></vaadin-icon>
          <span class="shortcut-label">${label}</span>
        </span>
        <span class="shortcut-sublabel">${build}</span>
      </a>
      <vaadin-button
        class="shortcut-close"
        theme="icon small"
        aria-label="Close deployment ${label} shortcut"
        @click="${this.removeMonitorResult}"
      >
        <vaadin-icon icon="vaadin:close-small" theme="small"></vaadin-icon>
      </vaadin-button>
    `;
  }

  removeMonitorResult(e: Event) {
    // See env-detail-tab.removeEnvDetail: stops the enclosing vaadin-tabs
    // selecting the tab this handler is about to remove.
    e.stopPropagation();
    e.preventDefault();

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
