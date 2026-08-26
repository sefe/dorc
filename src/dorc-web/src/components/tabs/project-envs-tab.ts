import { LitElement } from 'lit';
import '@vaadin/icons';
import '@vaadin/icon';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { ProjectApiModel } from '../../apis/dorc-api';
import { urlForName } from '../../router/router';

@customElement('project-envs-tab')
export class ProjectEnvsTab extends LitElement {
  @property({ type: Object }) public project: ProjectApiModel | undefined;

  /** Light DOM so `vaadin-tab._onKeyUp` can find the anchor — see env-detail-tab. */
  protected createRenderRoot() {
    return this;
  }

  render() {
    const name = this.project?.ProjectName ?? '';
    return html`
      <a
        class="shortcut-link"
        href="${urlForName('project-envs', { id: String(name) })}"
        title="${name}"
      >
        <vaadin-icon
          class="shortcut-icon"
          icon="vaadin:records"
          theme="small"
        ></vaadin-icon>
        <span class="shortcut-label">${name}</span>
      </a>
      <vaadin-button
        class="shortcut-close"
        theme="icon small"
        aria-label="Close ${name} shortcut"
        @click="${this.removeProjEnvs}"
      >
        <vaadin-icon icon="vaadin:close-small" theme="small"></vaadin-icon>
      </vaadin-button>
    `;
  }

  removeProjEnvs(e: Event) {
    // See env-detail-tab.removeEnvDetail: stops the enclosing vaadin-tabs
    // selecting the tab this handler is about to remove.
    e.stopPropagation();
    e.preventDefault();

    const event = new CustomEvent('close-project-envs', {
      detail: {
        Project: this.project
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}
