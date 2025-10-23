import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { DaemonStatusApi, ServiceStatusApiModel } from '../../apis/dorc-api';

@customElement('daemon-controls')
export class DaemonControls extends LitElement {
  @property({ type: Object }) daemonDetails: ServiceStatusApiModel | undefined;

  @property({ type: Number })
  envId = 0;

  @property({ type: String })
  private error = '';

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    return html`
      <div>
        <vaadin-button
          id="start"
          title="Start"
          theme="icon"
          ?disabled="${this.startDisabled}"
          @click="${this.serviceStart}"
        >
          <vaadin-icon
            icon="vaadin:play"
            style="color: ${this.startDisabled
              ? 'lightgrey'
              : 'cornflowerblue'}"
          ></vaadin-icon>
        </vaadin-button>
        <vaadin-button
          title="Stop"
          theme="icon"
          ?disabled="${this.stopDisabled}"
          @click="${this.serviceStop}"
        >
          <vaadin-icon
            icon="vaadin:stop"
            style="color: ${this.stopDisabled ? 'lightgrey' : 'cornflowerblue'}"
          ></vaadin-icon>
        </vaadin-button>
        <vaadin-button
          title="Restart"
          theme="icon"
          ?disabled="${this.restartDisabled}"
          @click="${this.serviceRestart}"
        >
          <vaadin-icon
            icon="vaadin:refresh"
            style="color: ${this.restartDisabled
              ? 'lightgrey'
              : 'cornflowerblue'}"
          ></vaadin-icon>
        </vaadin-button>
        <span style="color: darkred">${this.error}</span>
      </div>
    `;
  }

  get startDisabled() {
    return this.daemonDetails?.ServiceStatus?.toLowerCase() === 'running';
  }

  get stopDisabled() {
    return this.daemonDetails?.ServiceStatus?.toLowerCase() === 'stopped';
  }

  get restartDisabled() {
    return this.daemonDetails?.ServiceStatus?.toLowerCase() === 'stopped';
  }

  serviceStart() {
    this.requestChange('start');
  }

  serviceStop() {
    this.requestChange('stop');
  }

  serviceRestart() {
    this.requestChange('restart');
  }

  requestChange(requestedChange: string) {
    if (this.daemonDetails !== undefined) {
      const api = new DaemonStatusApi();

      this.daemonDetails.ServiceStatus = requestedChange;
      this.updateParentWith(this.daemonDetails);
      api
        .daemonStatusPut({ serviceStatusApiModel: this.daemonDetails })
        .subscribe(
          (data: ServiceStatusApiModel) => {
            this.daemonDetails = data;
            this.updateParentWith(data);
          },
          (err: any) => {
            if (err.response.ExceptionMessage !== undefined)
              this.error = err.response.ExceptionMessage;
            else this.error = err.response;

            console.log(
              `Error while trying to ${requestedChange} on ${
                this.daemonDetails?.ServerName
              }: ${this.daemonDetails?.ServiceName}`
            );
          }
        );
    }
  }

  private updateParentWith(data: ServiceStatusApiModel) {
    const event = new CustomEvent('daemon-status-changed', {
      detail: data,
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}
