import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../application-daemons';
import { ApplicationDaemons } from '../application-daemons';
import { PageEnvBase } from './page-env-base';

@customElement('env-daemons')
export class EnvDaemons extends PageEnvBase {
  @property({ type: Boolean }) daemonsLoading = false;
  @property({ type: Boolean }) discovering = false;

  static get styles() {
    return css`
      :host {
        width: 100%;
        height: 100%;
        display: flex;
        flex-direction: column;
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
      }

      .left-buttons {
        display: flex;
        gap: 8px;
        align-items: center;
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Application Daemon Details"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; padding-right: 4px; margin: 0px; box-sizing: border-box;"
      >
        <div class="button-container">
          <div class="left-buttons">
            <vaadin-button
              @click="${this.loadDaemons}"
              .disabled="${!this.environment?.UserEditable || this.daemonsLoading || this.discovering}"
              title="Load status of mapped daemons for this environment"
            >
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
            Discover Daemons
          </vaadin-button>
        </div>
      </vaadin-details>
      <application-daemons
        id="app-daemons"
        .envName="${this.environmentName ?? ''}"
        .userEditable="${this.environment?.UserEditable ?? false}"
        @daemons-loaded="${this.daemonsLoaded}"
      >
      </application-daemons>
    `;
  }

  constructor() {
    super();
    super.loadEnvironmentInfo();
  }

  daemonsLoaded() {
    this.daemonsLoading = false;
    this.discovering = false;
  }

  loadDaemons() {
    const appDaemons = this.shadowRoot?.getElementById(
      'app-daemons'
    ) as ApplicationDaemons;
    appDaemons.envName = this.envContent?.EnvironmentName || '';
    this.daemonsLoading = true;
    this.discovering = false;
    appDaemons.loadDaemons();
  }

  discoverDaemons() {
    const appDaemons = this.shadowRoot?.getElementById(
      'app-daemons'
    ) as ApplicationDaemons;
    appDaemons.envName = this.envContent?.EnvironmentName || '';
    this.discovering = true;
    this.daemonsLoading = false;
    appDaemons.discoverDaemons();
  }
}
