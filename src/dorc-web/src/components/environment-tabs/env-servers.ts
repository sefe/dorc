import '@polymer/paper-toggle-button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/dialog';  
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { css, PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../add-edit-server';
import '../attach-server';
import '../attached-servers';
import { Notification } from '@vaadin/notification';
import { PageEnvBase } from './page-env-base';
import { ServerApiModel } from '../../apis/dorc-api';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';

@customElement('env-servers')
export class EnvServers extends PageEnvBase {

  @property({ type: Boolean }) private envReadOnly = false;

  @property({ type: Array }) private servers: Array<ServerApiModel> | undefined;

  @state()
  private attachServerDialogOpened = false;

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
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px;"
      >
        <div>
          <div class="inline">
            <vaadin-button
              title="Attach Server"
              @click="${this.openAttachServerDialog}"
              .disabled="${this.envReadOnly}">
                Attach Server
          </vaadin-button>
          <vaadin-dialog
            id='attach-server-dialog'
            header-title='Attach Server'
            .opened='${this.attachServerDialogOpened}'
            draggable
            @opened-changed='${(event: DialogOpenedChangedEvent) => {
              this.attachServerDialogOpened = event.detail.value;
            }}'
            ${dialogRenderer(this.renderAttachServerDialog, [])}
            ${dialogFooterRenderer(this.renderAttachServerFooter, [])}
          ></vaadin-dialog>
          </div>
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

  private renderAttachServerDialog = () => html`
    <attach-server
      .envId="${this.environmentId}"
      @server-attached="${this._serverAttached}"
    ></attach-server>
    `;

  private renderAttachServerFooter = () => html`
    <vaadin-button @click="${this.closeAttachServerDialog}"
      >Close</vaadin-button
    >
  `;


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


  _serverAttached(e: CustomEvent) {
    this.serverAttachSuccess(e.detail.message);
    this.closeAttachServerDialog();
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

  private openAttachServerDialog() {
    this.attachServerDialogOpened = true;
  }

  private closeAttachServerDialog() {
    this.attachServerDialogOpened = false;
  }

}
