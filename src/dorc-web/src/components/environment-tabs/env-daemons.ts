import '@polymer/paper-toggle-button';
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
  @property({ type: Boolean }) private daemonsLoading = false;

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
        border: 2px solid cornflowerblue;
        border-radius: 50%;
        animation: lds-ring 1.2s cubic-bezier(0.5, 0, 0.5, 1) infinite;
        border-color: cornflowerblue transparent transparent transparent;
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
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Application Daemon Details"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px;"
      >
        <table>
          <tr>
            <td>
              <vaadin-button
                @click="${this.loadDaemons}"
                .disabled="${!this.environment?.UserEditable}"
                >Load Daemons
              </vaadin-button>
            </td>
            <td>
              ${this.daemonsLoading
                ? html`
                    <div style="padding-left: 5px" class="lds-ring">
                      <div></div>
                      <div></div>
                      <div></div>
                      <div></div>
                    </div>
                  `
                : html``}
            </td>
          </tr>
        </table>
      </vaadin-details>
      <application-daemons
        id="app-daemons"
        .envName="${this.environmentName ?? ''}"
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
  }

  loadDaemons() {
    const appDaemons = this.shadowRoot?.getElementById(
      'app-daemons'
    ) as ApplicationDaemons;
    appDaemons.envName = this.envContent?.EnvironmentName || '';
    this.daemonsLoading = false;
    appDaemons.loadDaemons();
    this.daemonsLoading = true;
  }
}
