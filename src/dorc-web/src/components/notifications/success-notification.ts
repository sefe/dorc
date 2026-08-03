import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/dialog';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/notification';
import '@vaadin/text-area';
import '@vaadin/text-field';
import '@vaadin/vertical-layout';
import '@vaadin/horizontal-layout';
import { css, LitElement } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { NotificationOpenedChangedEvent } from '@vaadin/notification';
import { notificationRenderer } from '@vaadin/notification/lit';

@customElement('success-notification')
export class SuccessNotification extends LitElement {
  @state()
  private notificationOpened = false;

  @property({ type: String })
  private successMessage = '';

  static get styles() {
    return css``;
  }

  render() {
    return html`
      <vaadin-notification
        id="success-toast"
        theme="success"
        duration="0"
        position="bottom-start"
        .opened="${this.notificationOpened}"
        @opened-changed="${(e: NotificationOpenedChangedEvent) => {
          this.notificationOpened = e.detail.value;
        }}"
        ${notificationRenderer(this.successNotificationRenderer, [this.successMessage])}
      ></vaadin-notification>
    `;
  }

  private readonly successNotificationRenderer = () => html`
    <vaadin-horizontal-layout theme="spacing" style="align-items: start;">
      <div>${this.successMessage}</div>
      <vaadin-button
        theme="tertiary-inline"
        @click="${() => (this.notificationOpened = false)}"
        aria-label="Close"
      >
        <vaadin-icon icon="lumo:cross"></vaadin-icon>
      </vaadin-button>
    </vaadin-horizontal-layout>
  `;

  public open() {
    this.notificationOpened = true;
  }
}
