import { confirmPrompt } from '../confirm-prompt';
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
      :host {
        display: inline-flex;
        align-items: center;
        flex-wrap: nowrap;
        gap: var(--lumo-space-xs);
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
      vaadin-button:disabled,
      vaadin-button[disabled] {
        background-color: var(--dorc-border-color);
      }
    `;
  }

  render() {
    const styles = { color: this.disabled ? 'var(--dorc-bg-secondary)' : 'var(--dorc-error-color)' };
    return html`
      <vaadin-button
        title="Remove Access"
        aria-label="Remove Access"
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

  async removeAccess() {
    // Snapshot before awaiting: this control sits in a recycled grid cell, so
    // `this.accessControl` can belong to a different row by the time the user answers.
    const accessControl = this.accessControl;
    const answer = await confirmPrompt(`Remove Access from ${accessControl?.Name}?`);
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
