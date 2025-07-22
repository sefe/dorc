import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import '../dorc-icon.js';
import { html } from 'lit/html.js';
import { EnvironmentApiModel } from '../../apis/dorc-api';
import { AccessControlType } from '../../apis/dorc-api';

@customElement('env-controls')
export class EnvControls extends LitElement {
  @property({ type: Object }) envDetails: EnvironmentApiModel | undefined;

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
        title="Environment Access..."
        theme="icon"
        @click="${this.openAccessControl}"
      >
        <dorc-icon icon="lock" color="primary"></dorc-icon>
      </vaadin-button>
      <vaadin-button
        title="Environment Details"
        theme="icon"
        @click="${this.openEnvironmentDetails}"
      >
        <dorc-icon icon="environment" color="primary"></dorc-icon>
      </vaadin-button>
    `;
  }

  openAccessControl() {
    const event = new CustomEvent('open-access-control', {
      detail: {
        Name: this.envDetails?.EnvironmentName,
        Type: AccessControlType.NUMBER_1
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  openEnvironmentDetails() {
    const event = new CustomEvent('open-env-detail', {
      detail: {
        Environment: this.envDetails
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}
