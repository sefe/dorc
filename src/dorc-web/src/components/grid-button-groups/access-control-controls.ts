import { css, LitElement } from 'lit';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import '../dorc-icon.js';
import { html } from 'lit/html.js';
import { AccessControlApiModel } from '../../apis/dorc-api';

@customElement('access-control-controls')
export class AccessControlControls extends LitElement {
  @property({ type: Object }) accessControl: AccessControlApiModel | undefined;

  @property({ type: Boolean }) disabled = false;

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
      vaadin-button:disabled,
      vaadin-button[disabled] {
        background-color: #dde2e8;
      }
    `;
  }

  render() {
    return html`
      <vaadin-button
        title="Remove Access"
        theme="icon"
        @click="${this.removeAccess}"
        ?disabled="${this.disabled}"
      >
        <dorc-icon 
          icon="delete"
          color="${this.disabled ? 'neutral' : 'danger'}"
        ></dorc-icon>
      </vaadin-button>
    `;
  }

  removeAccess() {
    const answer = confirm(`Remove Access from ${this.accessControl?.Name}?`);
    if (answer) {
      const event = new CustomEvent('access-control-removed', {
        detail: {
          message: 'Access Control Removed!'
        }
      });
      this.dispatchEvent(event);
    }
  }
}
