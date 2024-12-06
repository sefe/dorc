import '@vaadin/button';
import '@vaadin/dialog';
import '@vaadin/icon';
import '@vaadin/text-area';
import { LitElement, PropertyValues, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { guard } from 'lit/directives/guard.js';
import { html } from 'lit/html.js';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';

@customElement('log-dialog')
export class LogDialog extends LitElement {
  @property()
  isOpened = false;

  @property()
  selectedLog: string | undefined;

  render() {
    return html`
      <vaadin-dialog
        .opened="${this.isOpened}"
        draggable="true"
        @opened-changed="${(event: DialogOpenedChangedEvent) => {
          this.isOpened = event.detail.value;
          if (!this.isOpened) {
            this.dispatchEvent(
              new CustomEvent('log-dialog-closed', {
                bubbles: true,
                composed: true
              })
            );
          }
        }}"
        resizable
        .renderer="${guard([], () => (root: HTMLElement) => {
          render(
            html`<vaadin-button
                @click="${() =>
                  this.dispatchEvent(
                    new CustomEvent('close-log-dialog', {
                      bubbles: true,
                      composed: true
                    })
                  )}"
              >
                <vaadin-icon
                  style="color: cornflowerblue;"
                  icon="vaadin:close-small"
                ></vaadin-icon>
              </vaadin-button>
              <div style="width: 97vw">
                <vaadin-text-area
                  style="width: 97%"
                  label="Log"
                  .value="${this.selectedLog ?? ''}"
                ></vaadin-text-area>
              </div>`,
            root
          );
        })}"
      ></vaadin-dialog>
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener('close-log-dialog', this.close as EventListener);
  }

  private close() {
    this.isOpened = false;
    this.dispatchEvent(
      new CustomEvent('log-dialog-closed', {
        bubbles: true,
        composed: true
      })
    );
  }
}
