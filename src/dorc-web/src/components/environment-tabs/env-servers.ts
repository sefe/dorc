import '@polymer/paper-toggle-button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, PropertyValues } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../add-edit-server';
import '../attach-server';
import '../attached-servers';
import { Notification } from '@vaadin/notification';
import { PageEnvBase } from './page-env-base';
import { ServerApiModel } from '../../apis/dorc-api';

@customElement('env-servers')
export class EnvServers extends PageEnvBase {
  @property({ type: Boolean }) private addServer = false;

  @property({ type: Boolean }) private attachServer = false;

  @property({ type: Boolean }) private envReadOnly = false;

  @property({ type: Array }) private servers: Array<ServerApiModel> | undefined;

  static get styles() {
    return css`
      :host {
        width: 100%;
      }
      .span {
        font-family: var(--lumo-font-family);
      }
      .center {
        margin: 10px 20px 10px;
        width: 100%;
        padding: 10px;
      }
      .inline {
        display: inline-block;
        vertical-align: middle;
      }
      .buttons {
        font-size: 10px;
        color: cornflowerblue;
        padding: 2px;
      }
      vaadin-details {
        overflow: auto;
        width: calc(100% - 4px);
        height: calc(100vh - 175px);
        --divider-color: rgb(223, 232, 239);
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Application Server Details"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px"
      >
        <div>
          <div class="inline">
            <div class="inline">
              <paper-toggle-button
                class="buttons"
                id="addDatabase"
                .checked="${this.addServer}"
                @click="${this._addServer}"
                .disabled="${this.envReadOnly}"
                >ADD
              </paper-toggle-button>
            </div>
            <div class="inline">
              <paper-toggle-button
                class="buttons"
                id="attachDatabase"
                .checked="${this.attachServer}"
                @click="${this._attachServer}"
                .disabled="${this.envReadOnly}"
                >ATTACH
              </paper-toggle-button>
            </div>
          </div>
          ${this.addServer
            ? html` <div class="center-aligned">
                <add-edit-server
                  .envId="${this.environmentId ?? 0}"
                  .attach="${this.addServer}"
                  @server-attached="${this._serverAdded}"
                ></add-edit-server>
              </div>`
            : html``}
          ${this.attachServer
            ? html` <div class="center-aligned">
                <attach-server
                  .envId="${this.environmentId}"
                  @server-attached="${this._serverAttached}"
                ></attach-server>
              </div>`
            : html``}
          <div>
            <attached-servers
              id="attached-servers"
              .envId="${this.environmentId ?? 0}"
              .envContent="${this.envContent}"
              .servers="${this.servers}"
              .readonly="${this.envReadOnly}"
            ></attached-servers>
          </div>
        </div>
      </vaadin-details>
    `;
  }

  constructor() {
    super();

    super.loadEnvironmentInfo();
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'environment-stale',
      this.environmentStale as EventListener
    );
    this.addEventListener(
      'server-tags-updated',
      this.environmentStale as EventListener
    );
  }

  private environmentStale() {
    this.refreshEnvDetails(this.environment);
  }

  _addServer() {
    this.addServer = !this.addServer;
    if (this.addServer) {
      this.attachServer = !this.addServer;
    }
  }

  _attachServer() {
    this.attachServer = !this.attachServer;
    if (this.attachServer) {
      this.addServer = !this.attachServer;
    }
  }

  _serverAdded(e: CustomEvent) {
    this.serverAttachSuccess(e.detail.message);
    this.addServer = false;
  }

  _serverAttached(e: CustomEvent) {
    this.serverAttachSuccess(e.detail.message);
    this.attachServer = false;
  }

  serverAttachSuccess(text: string) {
    this.environmentStale();
    Notification.show(text, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }

  override notifyEnvironmentContentReady() {
    this.servers =
      this.envContent?.AppServers !== null
        ? this.envContent?.AppServers
        : undefined;
    this.envReadOnly = !this.environment?.UserEditable;
  }
}
