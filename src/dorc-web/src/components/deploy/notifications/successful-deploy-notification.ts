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
import '../../dorc-icon.js';
import { html } from 'lit/html.js';
import {
  NotificationOpenedChangedEvent,
  NotificationRenderer
} from '@vaadin/notification';
import { DeploymentRequestApiModel } from '../../../apis/dorc-api';

@customElement('successful-deploy-notification')
export class SuccessfulDeployNotification extends LitElement {
  @state()
  private notificationOpened = false;

  @property({ type: String })
  private envName = '';

  @property({ type: String })
  private selectedBuild = '';

  @property({ type: String })
  private requestedDeploymentId = '';

  static get styles() {
    return css`
      vaadin-button {
        margin: 0px;
      }
    `;
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
        .renderer="${this.successNotificationRenderer}"
      ></vaadin-notification>
    `;
  }

  successNotificationRenderer: NotificationRenderer = root => {
    render(
      html`
        <vaadin-horizontal-layout>
          <div style="padding-right: 5px; margin: auto">
            Started deployment request with ID:
          </div>
          <vaadin-button
            @click="${() => {
              const req: DeploymentRequestApiModel = {
                Id: Number.parseInt(this.requestedDeploymentId, 10),
                BuildNumber: this.selectedBuild.replace(' [PINNED]', ''),
                EnvironmentName: this.envName
              };

              const event = new CustomEvent('open-monitor-result', {
                detail: {
                  request: req,
                  message: 'Show results for Request'
                },
                bubbles: true,
                composed: true
              });
              this.dispatchEvent(event);
            }}"
            theme="primary"
          >
            <dorc-icon icon="clipboard"></dorc-icon>
            ${this.requestedDeploymentId}
          </vaadin-button>
          <vaadin-button
            theme="tertiary-inline"
            @click="${() => (this.notificationOpened = false)}"
            aria-label="Close"
          >
            <dorc-icon icon="close-small"></dorc-icon>
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
