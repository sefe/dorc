import '@vaadin/button';
import '@vaadin/horizontal-layout';
import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import './dorc-icon.js';
import { html } from 'lit/html.js';
import './tags-input';

@customElement('dismissible-item')
export class DismissibleItem extends LitElement {
  @property({ type: String }) public message = '';

  static get styles() {
    return css`
      :host {
        display: flex;
        background: #c7e0ff;
      }
    `;
  }

  render() {
    return html`
      <vaadin-button
        theme="tertiary-inline"
        aria-label="Close"
        @click="${() => (this.style.display = 'none')}"
      >
        <dorc-icon icon="close"></dorc-icon>
      </vaadin-button>
      <div style="padding-top: 3px">${this.message}</div>
    `;
  }
}
