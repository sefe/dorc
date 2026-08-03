import { confirmPrompt } from '../confirm-prompt';
import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { RequestProperty } from '../../apis/dorc-api/index.js';
import '../../icons/iron-icons.js';

@customElement('property-override-controls')
export class PropertyOverrideControls extends LitElement {
  @property({ type: Object }) propertyOverride: RequestProperty | undefined;

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    return html`
      <vaadin-button
        title="Remove Property Override"
        aria-label="Remove Property Override"
        theme="icon"
        @click="${this.detailedResults}"
      >
        <vaadin-icon icon="icons:delete" style="color: var(--dorc-error-color)"></vaadin-icon>
      </vaadin-button>
    `;
  }

  async detailedResults() {
    // Snapshot before awaiting, and report the row in the detail: this control
    // sits in a recycled grid cell, and the listener the parent bound into that
    // cell is rebound on every re-render, so neither `this.propertyOverride`
    // nor the parent's closure can be trusted once the await returns.
    const propertyOverride = this.propertyOverride;
    const answer = await confirmPrompt('Remove Property Override?');
    if (answer) {
      const event = new CustomEvent('property-override-removed', {
        detail: {
          propertyOverride,
          message: 'Property Override Removed!'
        }
      });
      this.dispatchEvent(event);
    }
  }
}
