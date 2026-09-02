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
import { retrieveErrorMessage } from '../../helpers/errorMessage-retriever';

@customElement('error-notification')
export class ErrorNotification extends LitElement {
  private static activeNotification: ErrorNotification | undefined;

  @state()
  private notificationOpened = false;

  @property({ type: String })
  errorMessage = '';

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
          if (
            !e.detail.value &&
            ErrorNotification.activeNotification === this
          ) {
            ErrorNotification.activeNotification = undefined;
          }
        }}"
        ${notificationRenderer(this.errorNotificationRenderer, [this.errorMessage])}
      ></vaadin-notification>
    `;
  }

  private readonly errorNotificationRenderer = () => html`
    <vaadin-horizontal-layout theme="spacing" style="align-items: start;">
      <div>${retrieveErrorMessage(this.errorMessage)}</div>
      <vaadin-button
        theme="tertiary-inline"
        @click="${() => this.close()}"
        aria-label="Close"
      >
        <vaadin-icon icon="lumo:cross"></vaadin-icon>
      </vaadin-button>
    </vaadin-horizontal-layout>
  `;

  public open() {
    const activeNotification = ErrorNotification.activeNotification;
    if (
      activeNotification &&
      activeNotification !== this &&
      activeNotification.isConnected
    ) {
      const activeMessage = retrieveErrorMessage(
        activeNotification.errorMessage
      );
      const incomingMessage = retrieveErrorMessage(this.errorMessage);
      if (activeMessage === incomingMessage) {
        this.remove();
        return;
      }
    }

    ErrorNotification.activeNotification = this;
    this.notificationOpened = true;
  }

  disconnectedCallback(): void {
    super.disconnectedCallback();
    if (ErrorNotification.activeNotification === this) {
      ErrorNotification.activeNotification = undefined;
    }
  }

  private close() {
    this.notificationOpened = false;
    if (ErrorNotification.activeNotification === this) {
      ErrorNotification.activeNotification = undefined;
    }
  }
}
