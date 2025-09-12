import { css, LitElement, PropertyValues, render } from 'lit';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { GridItemModel } from '@vaadin/grid';
import './grid-button-groups/daemon-controls';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { DaemonStatusApi } from '../apis/dorc-api';
import { ServiceStatusApiModel } from '../apis/dorc-api';

@customElement('application-daemons')
export class ApplicationDaemons extends LitElement {
  @property({ type: String })
  _envName = '';

  @property({ type: Array })
  private daemonsAndStatuses: ServiceStatusApiModel[] | undefined;

  get envName() {
    return this._envName;
  }

  set envName(envName: string) {
    this._envName = envName;
    console.log(`setting envName to ${envName}`);
  }

  static get styles() {
    return css`
      :host {
        height: 100%;
        display: flex;
      }
      vaadin-grid#grid {
        overflow: hidden;
        height: 100%;
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    return html`
      <vaadin-grid
        id="grid"
        .items="${this.daemonsAndStatuses}"
        theme="compact row-stripes no-row-borders no-border"
        multi-sort
      >
        <vaadin-grid-sort-column
          path="ServerName"
          header="Server Name"
          resizable
          width="150px"
          flex-grow="0"
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          path="ServiceName"
          header="Daemon Name"
          resizable
          width="300px"
          flex-grow="0"
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          path="ServiceStatus"
          header="Status"
          resizable
          width="100px"
          flex-grow="0"
          .renderer="${this._daemonStatusRenderer}"
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-column
          .renderer="${this._boundDaemonsButtonsRenderer}"
          .attachedAppDaemonControl="${this}"
        >
        </vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  _daemonStatusRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<ServiceStatusApiModel>
  ) {
    const daemon = model.item as ServiceStatusApiModel;
    const status = daemon?.ServiceStatus?.toLowerCase();
    if (status === 'running') {
      root.style.color = 'green';
    } else if (status === 'stopped') {
      root.style.color = 'black';
    } else {
      root.style.color = 'red';
    }
    render(html`<span>${daemon?.ServiceStatus}</span>`, root);
  }

  _boundDaemonsButtonsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<ServiceStatusApiModel>
  ) {
    const daemon = model.item as ServiceStatusApiModel;
    render(
      html`<daemon-controls .daemonDetails="${daemon}"></daemon-controls>`,
      root
    );
  }

  public loadDaemons() {
    const api = new DaemonStatusApi();
    api.daemonStatusEnvNameGet({ envName: this.envName }).subscribe({
      next: (data: ServiceStatusApiModel[]) => {
        this.setServiceStatuses(data);
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading daemon statuses')
    });
  }

  protected firstUpdated(_changedProperties: PropertyValues): void {
    super.firstUpdated(_changedProperties);
    this.addEventListener(
      'daemon-status-changed',
      this.daemonStatusUpdated as EventListener
    );
  }

  daemonStatusUpdated(event: CustomEvent<ServiceStatusApiModel>) {
    const daemonData = event.detail as ServiceStatusApiModel;
    const index = this.daemonsAndStatuses?.findIndex(
      daemon =>
        daemon.ServiceName === daemonData.ServiceName &&
        daemon.ServerName === daemonData.ServerName
    );
    if (index !== undefined && index > -1) {
      const updatedDaemons = [...this.daemonsAndStatuses!];
      updatedDaemons[index] = daemonData;
      this.daemonsAndStatuses = updatedDaemons;
    }
  }

  private setServiceStatuses(data: ServiceStatusApiModel[]) {
    this.daemonsAndStatuses = data;
    const event = new CustomEvent('daemons-loaded', {
      detail: {
        message: ''
      }
    });
    this.dispatchEvent(event);
  }
}
