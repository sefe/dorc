import { css, LitElement } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import { GridItemModel } from '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { render } from 'lit';
import { Notification } from '@vaadin/notification';
import type { DaemonApiModel, ServerApiModel } from '../apis/dorc-api';
import { RefDataDaemonsApi } from '../apis/dorc-api';
import { ServerDaemonsApi } from '../apis/dorc-api/apis/ServerDaemonsApi';

@customElement('map-daemons')
export class ServerDaemonMapping extends LitElement {
  @property({ type: Object })
  get server(): ServerApiModel | undefined {
    return this._server;
  }

  set server(value: ServerApiModel | undefined) {
    const oldVal = this._server;
    this._server = value;
    if (value?.ServerId && value.ServerId !== oldVal?.ServerId) {
      this.loadMappedDaemons();
    }
    this.requestUpdate('server', oldVal);
  }

  private _server: ServerApiModel | undefined;

  @state()
  private mappedDaemons: DaemonApiModel[] = [];

  @state()
  private allDaemons: DaemonApiModel[] = [];

  @state()
  private selectedDaemonId: number | undefined;

  @property({ type: Boolean })
  readonly = false;

  static get styles() {
    return css`
      :host {
        display: block;
        padding: 10px;
      }
      vaadin-grid#mapped-daemons-grid {
        overflow: hidden;
        max-height: 400px;
        --divider-color: rgb(223, 232, 239);
      }
      .attach-row {
        display: flex;
        align-items: baseline;
        gap: 8px;
        margin-bottom: 10px;
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  constructor() {
    super();
    this.loadAllDaemons();
  }

  render() {
    const unmappedDaemons = this.allDaemons.filter(
      d => !this.mappedDaemons.some(m => m.Id === d.Id)
    );

    return html`
      <div class="attach-row">
        <vaadin-combo-box
          label="Map Daemon"
          item-value-path="Id"
          item-label-path="DisplayName"
          .items="${unmappedDaemons}"
          ?disabled="${this.readonly}"
          @value-changed="${this.onDaemonSelected}"
          placeholder="Select Daemon"
          style="width: 300px"
          clear-button-visible
        ></vaadin-combo-box>
        <vaadin-button
          ?disabled="${this.readonly || !this.selectedDaemonId}"
          @click="${this.attachDaemon}"
        >
          <vaadin-icon
            icon="vaadin:link"
            style="color: cornflowerblue"
          ></vaadin-icon>
          Map
        </vaadin-button>
      </div>
      <vaadin-grid
        id="mapped-daemons-grid"
        .items="${this.mappedDaemons}"
        theme="compact row-stripes no-row-borders no-border"
        all-rows-visible
      >
        <vaadin-grid-column
          path="Name"
          header="Daemon Name"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="DisplayName"
          header="Display Name"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="ServiceType"
          header="Type"
          resizable
          auto-width
          flex-grow="0"
        ></vaadin-grid-column>
        <vaadin-grid-column
          width="100px"
          flex-grow="0"
          .renderer="${this._detachRenderer}"
          .mappingControl="${this}"
        ></vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  private _detachRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DaemonApiModel>
  ) {
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const control = _column.mappingControl as ServerDaemonMapping;
    const daemon = model.item as DaemonApiModel;
    render(
      html`
        <vaadin-button
          title="Unmap daemon"
          theme="icon"
          ?disabled="${control.readonly}"
          @click="${() => control.detachDaemon(daemon)}"
        >
          <vaadin-icon
            icon="vaadin:unlink"
            style="color: ${control.readonly ? 'grey' : '#FF3131'}"
          ></vaadin-icon>
        </vaadin-button>
      `,
      root
    );
  }

  private onDaemonSelected(e: CustomEvent) {
    const value = e.detail.value as number;
    this.selectedDaemonId = value || undefined;
  }

  private loadAllDaemons() {
    const api = new RefDataDaemonsApi();
    api.refDataDaemonsGet().subscribe({
      next: (data: DaemonApiModel[]) => {
        this.allDaemons = data;
      },
      error: (err: any) => console.error(err)
    });
  }

  private loadMappedDaemons() {
    if (!this._server?.ServerId) return;
    const api = new ServerDaemonsApi();
    api
      .serverDaemonsServerIdGet({ serverId: this._server.ServerId })
      .subscribe({
        next: (data: DaemonApiModel[]) => {
          this.mappedDaemons = data;
        },
        error: (err: any) => console.error(err)
      });
  }

  private attachDaemon() {
    if (!this._server?.ServerId || !this.selectedDaemonId) return;
    const api = new ServerDaemonsApi();
    api
      .serverDaemonsPost({
        serverId: this._server.ServerId,
        daemonId: this.selectedDaemonId
      })
      .subscribe({
        next: () => {
          Notification.show('Daemon mapped to server', {
            theme: 'success',
            position: 'bottom-start',
            duration: 3000
          });
          this.selectedDaemonId = undefined;
          this.loadMappedDaemons();
          this.fireMappingChanged();
        },
        error: (err: any) => console.error(err)
      });
  }

  public detachDaemon(daemon: DaemonApiModel) {
    if (!this._server?.ServerId || !daemon.Id) return;
    const answer = confirm(
      `Unmap daemon "${daemon.DisplayName ?? daemon.Name}" from server "${this._server.Name}"?`
    );
    if (!answer) return;

    const api = new ServerDaemonsApi();
    api
      .serverDaemonsDelete({
        serverId: this._server.ServerId,
        daemonId: daemon.Id
      })
      .subscribe({
        next: () => {
          Notification.show('Daemon unmapped from server', {
            theme: 'success',
            position: 'bottom-start',
            duration: 3000
          });
          this.loadMappedDaemons();
          this.fireMappingChanged();
        },
        error: (err: any) => console.error(err)
      });
  }

  private fireMappingChanged() {
    this.dispatchEvent(
      new CustomEvent('daemon-mapping-changed', {
        bubbles: true,
        composed: true
      })
    );
  }
}