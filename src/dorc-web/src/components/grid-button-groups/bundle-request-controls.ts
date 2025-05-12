import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/password-field';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { BundledRequestsApi, BundledRequestsApiModel } from '../../apis/dorc-api';
import '../../icons/editor-icons.js';
import '../../icons/iron-icons.js';

@customElement('bundle-request-controls')
export class VariableValueControls extends LitElement {
  @property({ type: Object })
  value!: BundledRequestsApiModel;

  static get styles() {
    return css`
      :host{
        width: 100%;
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    return html`
      <vaadin-button
        id="edit"
        title="Edit"
        theme="icon small"
        @click="${this._editClick}"
      >
        <vaadin-icon
          icon="editor:mode-edit"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        title="Delete Value"
        theme="icon small"
        @click="${this.removeBundleRequest}"
      >
        <vaadin-icon
          icon="icons:clear"
        ></vaadin-icon>
      </vaadin-button>
    `;
  }

  removeBundleRequest() {
    const answer = confirm(
      `Confirm removing value: ${this.value?.RequestName}?\nfor Bundle: ${
        this.value?.BundleName}`
    );
    if (answer && this.value?.Id) {
      const api = new BundledRequestsApi();
      api.bundledRequestsDelete({
          id: this.value.Id
        })
        .subscribe({
          next: () => {
            this.fireVariableValueDeletedEvent();
          },
          error: () => this.fireVariableValueDeletedEvent(),
          complete: () => console.log('done deleting variable value')
        });
    }
  }

  private fireVariableValueDeletedEvent() {
    const event = new CustomEvent('variable-value-deleted', {
      detail: {
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  _editClick() {

  }
}
