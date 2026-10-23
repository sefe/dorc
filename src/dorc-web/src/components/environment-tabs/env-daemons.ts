import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import { css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../application-daemons';
import { ApplicationDaemons } from '../application-daemons';
import { PageEnvBase } from './page-env-base';

@customElement('env-daemons')
export class EnvDaemons extends PageEnvBase {
  @property({ type: Boolean }) daemonsLoading = false;
  @property({ type: Boolean }) discovering = false;
  @state() private daemonCount = 0;

  static get styles() {
    return css`
      :host {
        width: 100%;
        height: 100%;
        display: flex;
        flex-direction: column;
      }

      vaadin-details {
        overflow: hidden;
        width: calc(100% - 4px);
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
      }

      vaadin-details::part(content) {
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
        overflow: hidden;
      }

      .details-content {
        display: flex;
        flex-direction: column;
        flex: 1;
        min-height: 0;
      }

      .lds-ring {
        display: inline-block;
        position: relative;
        width: 20px;
        height: 20px;
      }

      .lds-ring div {
        box-sizing: border-box;
        display: block;
        position: absolute;
        width: 16px;
        height: 16px;
        margin: 2px;
        border: 2px solid var(--dorc-link-color);
        border-radius: 50%;
        animation: lds-ring 1.2s cubic-bezier(0.5, 0, 0.5, 1) infinite;
        border-color: var(--dorc-link-color) transparent transparent transparent;
      }

      .lds-ring div:nth-child(1) {
        animation-delay: -0.45s;
      }

      .lds-ring div:nth-child(2) {
        animation-delay: -0.3s;
      }

      .lds-ring div:nth-child(3) {
        animation-delay: -0.15s;
      }

      @keyframes lds-ring {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }

      .button-container {
        display: flex;
        justify-content: space-between;
        align-items: center;
        width: 100%;
        padding: var(--lumo-space-s) 0;
      }

      .left-buttons {
        display: flex;
        gap: 8px;
        align-items: center;
      }

      application-daemons {
        flex: 1;
        min-height: 0;
      }

      .summary-bar {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-s);
        font-weight: 400;
      }

      .summary-bar vaadin-icon {
        color: var(--dorc-link-color);
      }

      .count-badge {
        background-color: var(--dorc-link-color);
        color: var(--dorc-bg-primary);
        border-radius: 1em;
        font-size: var(--lumo-font-size-xs);
        font-weight: 600;
        padding: 0 var(--lumo-space-s);
        line-height: 1.6;
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; padding-right: 4px; margin: 0px; box-sizing: border-box;"
      >
        <vaadin-details-summary slot="summary">
          <div class="summary-bar">
            <vaadin-icon icon="vaadin:cogs" theme="small"></vaadin-icon>
            Application Daemon Details
            <span class="count-badge">${this.daemonCount}</span>
          </div>
        </vaadin-details-summary>
        <div class="details-content">
          <div class="button-container">
            <div class="left-buttons">
              <vaadin-button
                @click="${this.loadDaemons}"
                .disabled="${!this.environment?.UserEditable || this.daemonsLoading || this.discovering}"
                title="Load status of mapped daemons for this environment"
              >
                <vaadin-icon icon="vaadin:refresh" slot="prefix"></vaadin-icon>
                Load Daemons
              </vaadin-button>
              ${
                this.daemonsLoading || this.discovering
                  ? html`
                      <div class="lds-ring">
                        <div></div>
                        <div></div>
                        <div></div>
                        <div></div>
                      </div>
                    `
                  : html``
              }
            </div>
            <vaadin-button
              @click="${this.discoverDaemons}"
              .disabled="${!this.environment?.UserEditable || this.daemonsLoading || this.discovering}"
              title="Scan all servers and automatically create daemon-server mappings for discovered services"
            >
              <vaadin-icon icon="vaadin:search" slot="prefix"></vaadin-icon>
              Discover Daemons
            </vaadin-button>
          </div>
          <application-daemons
            id="app-daemons"
            .envName="${this.environmentName ?? ''}"
            .userEditable="${this.environment?.UserEditable ?? false}"
            @daemons-loaded="${this.daemonsLoaded}"
          >
          </application-daemons>
        </div>
      </vaadin-details>
    `;
  }

  constructor() {
    super();
    super.loadEnvironmentInfo();
  }

  daemonsLoaded() {
    const appDaemons = this.shadowRoot?.getElementById(
      'app-daemons'
    ) as ApplicationDaemons;
    this.daemonCount = appDaemons.daemonsAndStatuses?.length ?? 0;
    this.daemonsLoading = false;
    this.discovering = false;
  }

  loadDaemons() {
    const appDaemons = this.shadowRoot?.getElementById(
      'app-daemons'
    ) as ApplicationDaemons;
    appDaemons.envName =
      this.envContent?.EnvironmentName ||
      this.environment?.EnvironmentName ||
      this.environmentName ||
      '';
    this.daemonsLoading = true;
    this.discovering = false;
    appDaemons.loadDaemons();
  }

  discoverDaemons() {
    const appDaemons = this.shadowRoot?.getElementById(
      'app-daemons'
    ) as ApplicationDaemons;
    appDaemons.envName =
      this.envContent?.EnvironmentName ||
      this.environment?.EnvironmentName ||
      this.environmentName ||
      '';
    this.discovering = true;
    this.daemonsLoading = false;
    appDaemons.discoverDaemons();
  }
}
