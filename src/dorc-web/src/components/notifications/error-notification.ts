import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/dialog';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/notification';
import '@vaadin/text-area';
import '@vaadin/text-field';
import '@vaadin/vertical-layout';
import '@vaadin/horizontal-layout';
import { css, LitElement, render } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import '../dorc-icon.js';
import { html } from 'lit/html.js';
import {
  NotificationOpenedChangedEvent,
  NotificationRenderer
} from '@vaadin/notification';

@customElement('error-notification')
export class ErrorNotification extends LitElement {
  @state()
  private notificationOpened = false;

  @property({ type: String })
  private errorMessage = '';

  static get styles() {
    return css``;
  }

  render() {
    return html`
      <vaadin-notification
        id="error-toast"
        theme="error"
        duration="0"
        position="bottom-start"
        .opened="${this.notificationOpened}"
        @opened-changed="${(e: NotificationOpenedChangedEvent) => {
          this.notificationOpened = e.detail.value;
        }}"
        .renderer="${this.errorNotificationRenderer}"
      ></vaadin-notification>
    `;
  }

  errorNotificationRenderer: NotificationRenderer = root => {
    render(
      html`
        <vaadin-horizontal-layout theme="spacing" style="align-items: start;">
          <div>${this.errorMessage}</div>
          <vaadin-button
            theme="tertiary-inline"
            @click="${() => (this.notificationOpened = false)}"
            aria-label="Close"
          >
            <dorc-icon icon="close"></dorc-icon>
          </vaadin-button>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  public open() {
    this.notificationOpened = true;
  }
}
