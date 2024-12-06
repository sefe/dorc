import { css, LitElement } from 'lit';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { RequestPostRequest } from '../../apis/dorc-api';
import '../tags-input';
import { HegsDialog } from '../hegs-dialog';
import '../hegs-dialog';
import '../hegs-json-viewer';
import { HegsJsonViewer } from '../hegs-json-viewer';

@customElement('deploy-confirm-dialog')
export class DeployConfirmDialog extends LitElement {
  @property({ type: Object })
  deployJson!: RequestPostRequest;
  @state()
  _open = false;
  @query('#dialog') dialog!: HegsDialog;

  static get styles() {
    return css``;
  }

  render() {
    return html`
      <hegs-dialog
        id="dialog"
        title="New deployment"
        .open="${this._open}"
        @hegs-dialog-closed="${this.Close}"
      >
        Please confirm you want to submit this deployment request?
        <hegs-json-viewer id="jsonviewer">{}</hegs-json-viewer>
        <vaadin-button @click="${() => (this._open = false)}">
          Cancel
        </vaadin-button>
        <vaadin-button
          theme="primary"
          @click="${() => {
            this._open = false;
            const event = new CustomEvent('deploy-confirm-dialog-begin', {
              detail: {},
              bubbles: true,
              composed: true
            });
            this.dispatchEvent(event);
          }}"
        >
          Deploy
        </vaadin-button>
      </hegs-dialog>
    `;
  }

  Open() {
    const jsonViewer = this.shadowRoot?.getElementById(
      'jsonviewer'
    ) as HegsJsonViewer;
    Object.assign(jsonViewer.data, this.deployJson.requestDto);
    jsonViewer.expand('**');

    this._open = true;
  }

  Close() {
    const event = new CustomEvent('deploy-confirm-dialog-closed', {
      detail: {},
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
    this._open = false;
  }
}
