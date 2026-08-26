import { LitElement } from 'lit';
import '@vaadin/icons';
import '@vaadin/icon';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { EnvironmentApiModel } from '../../apis/dorc-api';
import { urlForName } from '../../router/router';
import '../../icons/hardware-icons.js';

@customElement('env-detail-tab')
export class EnvDetailTab extends LitElement {
  @property({ type: Object }) public env: EnvironmentApiModel | undefined;

  /**
   * Renders into light DOM (D-03). `vaadin-tab._onKeyUp` activates a shortcut by
   * calling `this.querySelector('a')` — a *descendant* query — so an anchor inside
   * this component's shadow root is invisible to it and Enter selects the tab
   * without ever navigating. Moving the anchor into light DOM makes it a
   * descendant of the tab and restores keyboard activation.
   *
   * Consequence: `static styles` no longer applies. Shortcut styling lives in
   * `dorc-navbar`'s stylesheet (`.shortcut-*`), whose shadow root now contains
   * these elements — one scoped stylesheet rather than inline style soup.
   */
  protected createRenderRoot() {
    return this;
  }

  render() {
    const name = this.env?.EnvironmentName ?? '';
    return html`
      <a
        class="shortcut-link"
        href="${urlForName('environment', { id: String(name) })}"
        title="${name}"
      >
        <vaadin-icon
          class="shortcut-icon"
          icon="hardware:developer-board"
          theme="small"
        ></vaadin-icon>
        <span class="shortcut-label">${name}</span>
      </a>
      <vaadin-button
        class="shortcut-close"
        theme="icon small"
        aria-label="Close ${name} shortcut"
        @click="${this.removeEnvDetail}"
      >
        <vaadin-icon icon="vaadin:close-small" theme="small"></vaadin-icon>
      </vaadin-button>
    `;
  }

  removeEnvDetail(e: Event) {
    // Keep the click away from the enclosing vaadin-tabs: ListMixin._onClick
    // reads composedPath() and would select the tab this handler is about to
    // remove, leaving the drawer highlighting an unrelated item. _onClick bails
    // on defaultPrevented, so both calls are needed.
    e.stopPropagation();
    e.preventDefault();

    const event = new CustomEvent('close-env-detail', {
      detail: {
        Environment: this.env
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}
