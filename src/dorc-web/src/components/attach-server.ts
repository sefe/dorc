import { css, LitElement } from 'lit';
import { ComboBoxItemModel } from '@vaadin/combo-box';
import '@vaadin/button';
import '@vaadin/combo-box';
import '@polymer/paper-dialog';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import {
  RefDataEnvironmentsDetailsApi,
  RefDataServersApi
} from '../apis/dorc-api/apis';
import { ApiBoolResult, ServerApiModel } from '../apis/dorc-api';

@customElement('attach-server')
export class AttachServer extends LitElement {
  @property({ type: Array }) envDetails = [];

  @property({ type: Array }) private servers: Array<ServerApiModel> | undefined;

  @property({ type: Boolean }) private canSubmit = false;

  @property({ type: Object }) private selectedServer:
    | ServerApiModel
    | undefined;

  @property({ type: Number }) private envId: number | undefined;

  private serversMap: Map<number | undefined, ServerApiModel> | undefined;

  private finishedEvent: CustomEvent<{ message: string }> | undefined;

  constructor() {
    super();

    const api = new RefDataServersApi();
    api.refDataServersGetAllGet().subscribe({
      next: (data: Array<ServerApiModel>) => {
        this.setServers(data);
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading servers')
    });
  }

  static get styles() {
    return css``;
  }

  render() {
    return html`
      <div>
        <div class="inline">
          <vaadin-combo-box
            id="servers"
            label="Servers"
            item-value-path="ServerId"
            item-label-path="Name"
            @value-changed="${this.setSelectedServer}"
            .items="${this.servers}"
            filter-property="Name"
            .renderer="${this._boundServersRenderer}"
            placeholder="Select Server"
            style="width: 300px"
            clear-button-visible
          >
          </vaadin-combo-box>
          <vaadin-button
            .disabled="${!this.canSubmit}"
            @click="${this.attachServer}"
            >Attach</vaadin-button
          >
          <div>
            <h3 style="color: cornflowerblue">
              Server Name:
              <span style="color: black">${this.selectedServer?.Name}</span>
            </h3>
            <h3 style="color: cornflowerblue">
              Server OS:
              <span style="color: black">${this.selectedServer?.OsName}</span>
            </h3>
            <h3 style="color: cornflowerblue">
              Server Applications:
              <span style="color: black"
                >${this.selectedServer?.ApplicationTags}</span
              >
            </h3>
          </div>
        </div>
      </div>
    `;
  }

  _boundServersRenderer(
    root: HTMLElement,
    _: HTMLElement,
    { item }: ComboBoxItemModel<ServerApiModel>
  ) {
    // only render the checkbox once, to avoid re-creating during subsequent calls
    const serverApiModel = item as ServerApiModel;
    root.innerHTML = `<div>${serverApiModel.Name}</div>`;
  }

  setSelectedServer(data: CustomEvent) {
    const server = data.detail.value as number;
    this.selectedServer = this.serversMap?.get(server);
    this.canSubmit = true;
  }

  attachServer() {
    const api = new RefDataEnvironmentsDetailsApi();

    api
      .refDataEnvironmentsDetailsPut({
        envId: this.envId || 0,
        componentId: this.selectedServer?.ServerId || 0,
        action: 'attach',
        component: 'server'
      })
      .subscribe({
        next: (data: ApiBoolResult) => {
          this.serverAttachedComplete(data);
        },
        error: (err: any) => {
          console.error(err);
        },
        complete: () => console.log('done adding server')
      });
  }

  _reset() {
    this.canSubmit = false;
  }

  serverAttachedComplete(result: ApiBoolResult) {
    const id = result.Result as boolean;
    if (id) {
      this.finishedEvent = new CustomEvent('server-attached', {
        detail: {
          message: `Server ${this.selectedServer?.Name} added successfully!`
        }
      });
      this.dispatchEvent(this.finishedEvent);
    } else {
      const event = new CustomEvent('error-alert', {
        detail: {
          description: `Unable to attach server ${this.selectedServer?.Name}: `,
          result
        },
        bubbles: true,
        composed: true
      });
      this.dispatchEvent(event);
    }
    this._reset();
    this.canSubmit = false;
  }

  private setServers(data: Array<ServerApiModel>) {
    this.servers = data;
    this.serversMap = new Map(data.map(obj => [obj.ServerId, obj]));
  }
}
