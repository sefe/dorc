import '@vaadin/button';
import '@vaadin/horizontal-layout';
import '@vaadin/icon';
import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
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
        <vaadin-icon icon="lumo:cross"></vaadin-icon>
      </vaadin-button>
      <div style="padding-top: 3px">${this.message}</div>
    `;
  }
}
