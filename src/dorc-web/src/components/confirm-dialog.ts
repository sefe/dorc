import { css, LitElement } from 'lit';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/button';
import './hegs-dialog';
import { HegsDialog } from './hegs-dialog';

@customElement('confirm-dialog')
export class ConfirmDialog extends LitElement {
  @property({ type: String })
  title = 'Confirm Action';

  @property({ type: String })
  message = 'Are you sure you want to continue?';

  @property({ type: String })
  confirmText = 'Confirm';

  @property({ type: String })
  cancelText = 'Cancel';

  @state()
  _open = false;

  @query('#dialog') dialog!: HegsDialog;

  static get styles() {
    return css`
      .button-container {
        display: flex;
        gap: 10px;
        justify-content: flex-end;
        margin-top: 20px;
      }
      
      .message {
        margin: 20px 0;
        font-size: 16px;
      }
    `;
  }

  render() {
    return html`
      <hegs-dialog
        id="dialog"
        title="${this.title}"
        .open="${this._open}"
        @hegs-dialog-closed="${this.close}"
      >
        <div class="message">${this.message}</div>
        <div class="button-container">
          <vaadin-button @click="${this.cancel}">
            ${this.cancelText}
          </vaadin-button>
          <vaadin-button
            theme="primary error"
            @click="${this.confirm}"
          >
            ${this.confirmText}
          </vaadin-button>
        </div>
      </hegs-dialog>
    `;
  }

  public open() {
    this._open = true;
  }

  public close() {
    this._open = false;
  }

  private cancel() {
    this.close();
    const event = new CustomEvent('confirm-dialog-cancel', {
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  private confirm() {
    this.close();
    const event = new CustomEvent('confirm-dialog-confirm', {
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}