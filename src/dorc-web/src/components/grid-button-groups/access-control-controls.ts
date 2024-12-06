import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { styleMap } from 'lit/directives/style-map.js';
import { AccessControlApiModel } from '../../apis/dorc-api';
import '../../icons/iron-icons.js';

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
    const styles = { color: this.disabled ? '#F3F5F7' : '#FF3131' };
    return html`
      <vaadin-button
        title="Remove Access"
        theme="icon"
        @click="${this.removeAccess}"
        ?disabled="${this.disabled}"
      >
        <vaadin-icon
          icon="icons:delete"
          style=${styleMap(styles)}
        ></vaadin-icon>
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
