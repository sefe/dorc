import '@polymer/paper-toggle-button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../application-daemons';
import { ApplicationDaemons } from '../application-daemons';
import { PageEnvBase } from './page-env-base';
import GlobalCache from '../../global-cache';
import { DaemonStatusApi } from '../../apis/dorc-api';
import { ErrorNotification } from '../notifications/error-notification';
import '../notifications/error-notification';
import { SuccessNotification } from '../notifications/success-notification';
import '../notifications/success-notification';

@customElement('env-daemons')
export class EnvDaemons extends PageEnvBase {
  @property({ type: Boolean }) private daemonsLoading = false;

  @state() private isAdmin = false;

  @state() private discovering = false;

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
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Application Daemon Details"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; margin: 0px;"
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
              <vaadin-button
                ?hidden="${!this.isAdmin}"
                .disabled="${this.discovering}"
                title="Probe all servers in this environment and persist discovered daemon mappings"
                @click="${this.discoverDaemons}"
                >Discover Daemons
              </vaadin-button>
            </td>
            <td>
              ${this.daemonsLoading || this.discovering
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
        .userEditable="${this.environment?.UserEditable ?? false}"
        @daemons-loaded="${this.daemonsLoaded}"
      >
      </application-daemons>
    `;
  }

  constructor() {
    super();

    super.loadEnvironmentInfo();
    this.getUserRoles();
  }

  private getUserRoles() {
    const gc = GlobalCache.getInstance();
    if (gc.userRoles === undefined) {
      gc.allRolesResp?.subscribe({
        next: (userRoles: string[]) => {
          this.setUserRoles(userRoles);
        },
        error: (err: string) => console.error(err),
        complete: () => console.log('done loading user roles')
      });
    } else {
      this.setUserRoles(gc.userRoles);
    }
  }

  private setUserRoles(userRoles: string[]) {
    this.isAdmin = userRoles.find(p => p === 'Admin') !== undefined;
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

  private discoverDaemons() {
    const envName = this.envContent?.EnvironmentName || this.environmentName;
    if (!envName) return;

    this.discovering = true;
    const api = new DaemonStatusApi();
    api.daemonStatusDiscoverEnvNamePost({ envName }).subscribe({
      next: () => {
        const notification = new SuccessNotification();
        notification.setAttribute(
          'successMessage',
          `Daemon discovery completed for '${envName}'.`
        );
        this.shadowRoot?.appendChild(notification);
        notification.open();
      },
      error: (err: any) => {
        const message = err?.response?.message ?? err?.message ?? String(err);
        const notification = new ErrorNotification();
        notification.setAttribute(
          'errorMessage',
          `Daemon discovery failed for '${envName}': ${message}`
        );
        this.shadowRoot?.appendChild(notification);
        notification.open();
        this.discovering = false;
      },
      complete: () => {
        this.discovering = false;
      }
    });
  }
}
