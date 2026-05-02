import { css, html, LitElement } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import '@vaadin/button';
import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';
import { themeManager, Theme } from '../theme/theme-manager.ts';

@customElement('theme-toggle')
export class ThemeToggle extends LitElement {
  static get styles() {
    return css`
      :host {
        display: inline-flex;
        align-items: center;
      }

      vaadin-button {
        cursor: pointer;
      }
    `;
  }

  @state() private theme: Theme = themeManager.current;

  private unsubscribe?: () => void;

  connectedCallback() {
    super.connectedCallback();
    this.unsubscribe = themeManager.onChange(t => {
      this.theme = t;
    });
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    this.unsubscribe?.();
  }

  render() {
    const icon = this.theme === 'dark' ? 'vaadin:sun-o' : 'vaadin:moon-o';
    const label =
      this.theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode';

    return html`
      <vaadin-button
        theme="icon tertiary"
        aria-label="${label}"
        title="${label}"
        @click="${this.toggle}"
      >
        <vaadin-icon icon="${icon}"></vaadin-icon>
      </vaadin-button>
    `;
  }

  private toggle() {
    themeManager.toggle();
  }
}
