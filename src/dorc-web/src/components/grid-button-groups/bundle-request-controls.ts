import { LitElement, html, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import '@vaadin/button';
import '@vaadin/icon';
import '@vaadin/icons';
import '@vaadin/horizontal-layout';
import { BundledRequestsApiModel } from '../../apis/dorc-api';

@customElement('bundle-request-controls')
export class BundleRequestControls extends LitElement {
  static styles = css`
    vaadin-button {
      padding: 0px;
      margin: 0px;
    }

    vaadin-button:disabled,
    vaadin-button[disabled] {
      background-color: #dde2e8;
    }
  `;

  @property({ type: Boolean }) disabled = false;

  @property({ type: Object })
  value!: BundledRequestsApiModel;

  render() {
    return html`
      <vaadin-horizontal-layout theme="spacing">
        <vaadin-button
          theme="icon"
          @click="${() => {
            this.dispatchEvent(
              new CustomEvent('edit-bundle-request', {
                detail: {
                  value: this.value
                },
                bubbles: true,
                composed: true
              })
            );
          }}"
          ?disabled="${this.disabled}"
        >
          <vaadin-icon icon="editor:mode-edit"></vaadin-icon>
        </vaadin-button>
        <vaadin-button
          theme="icon error"
          @click="${this._handleDeleteClick}"
          ?disabled="${this.disabled}"
        >
          <vaadin-icon icon="icons:clear"></vaadin-icon>
        </vaadin-button>
      </vaadin-horizontal-layout>
    `;
  }

  private _handleDeleteClick() {
    this.dispatchEvent(
      new CustomEvent('delete-bundle-request', {
        detail: {
          value: this.value
        },
        bubbles: true,
        composed: true
      })
    );
  }
}
