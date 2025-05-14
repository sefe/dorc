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
    :host {
      display: block;
    }
    
    vaadin-button {
      margin-right: 4px;
    }
  `;

  @property({ type: Object })
  value!: BundledRequestsApiModel;

  render() {
    return html`
      <vaadin-horizontal-layout theme="spacing">
        <vaadin-button theme="tertiary small" @click="${() => {
          this.dispatchEvent(new CustomEvent('edit-click', {
            detail: { bundle: this.value },
            bubbles: true,
            composed: true
          }));
        }}">
          <vaadin-icon icon="editor:mode-edit"></vaadin-icon>
        </vaadin-button>
        <vaadin-button theme="tertiary small error" @click="${this._handleDeleteClick}">
          <vaadin-icon icon="icons:clear"></vaadin-icon>
        </vaadin-button>
      </vaadin-horizontal-layout>
    `;
  }

  private _handleDeleteClick() {
    this.dispatchEvent(new CustomEvent('delete-click', {
      detail: { bundleId: this.value.Id },
      bubbles: true,
      composed: true
    }));
  }
}
