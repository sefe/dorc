import { css, LitElement } from 'lit';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import '../dorc-icon.js';
import { html } from 'lit/html.js';
import { RequestProperty } from '../../apis/dorc-api/index.js';

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
        theme="icon"
        @click="${this.detailedResults}"
      >
        <dorc-icon icon="delete" color="#FF3131"></dorc-icon>
      </vaadin-button>
    `;
  }

  detailedResults() {
    const answer = confirm('Remove Property Override?');
    if (answer) {
      const event = new CustomEvent('property-override-removed', {
        detail: {
          message: 'Property Override Removed!'
        }
      });
      this.dispatchEvent(event);
    }
  }
}
