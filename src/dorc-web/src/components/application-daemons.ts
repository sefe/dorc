import { columnBodyRenderer } from '@vaadin/grid/lit';
import { css, LitElement, PropertyValues } from 'lit';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid';
import './grid-button-groups/daemon-controls';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { DaemonStatusApi } from '../apis/dorc-api';
import { DaemonStatusApiModel } from '../apis/dorc-api';

@customElement('application-daemons')
export class ApplicationDaemons extends LitElement {
  @property({ type: String })
  _envName = '';

  @property({ type: Array })
  private daemonsAndStatuses: DaemonStatusApiModel[] | undefined;

  @property({ type: Boolean })
  public userEditable = false;

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
        height: 100%
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
          path="DaemonName"
          header="Daemon Name"
          resizable
          width="300px"
          flex-grow="0"
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          path="Status"
          header="Status"
          resizable
          width="100px"
          flex-grow="0"
          ${columnBodyRenderer(this._daemonStatusRenderer, [])}
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-column
          ${columnBodyRenderer(this._boundDaemonsButtonsRenderer, [
            this.userEditable
          ])}
        >
        </vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  // The colour was set on the cell root; it lives on the span now, which is
  // the only thing a returned template can style.
  _daemonStatusRenderer(daemon: DaemonStatusApiModel) {
    const errorMessage = daemon?.ErrorMessage;
    const status = daemon?.Status?.toLowerCase();
    const colour =
      !errorMessage && status === 'running'
        ? 'var(--dorc-success-text)'
        : !errorMessage && status === 'stopped'
          ? 'var(--dorc-text-primary)'
          : 'var(--dorc-error-color)';

    return errorMessage
      ? html`<span style="color: ${colour}" title="${errorMessage}"
          >⚠ ${daemon?.Status ?? 'unreachable'}</span
        >`
      : html`<span style="color: ${colour}">${daemon?.Status}</span>`;
  }

  _boundDaemonsButtonsRenderer(
    item: DaemonStatusApiModel
  ) {
    const daemon = item as DaemonStatusApiModel;
    return html`<daemon-controls .daemonDetails="${daemon}" .userEditable="${this.userEditable}"></daemon-controls>`;
  }

  public loadDaemons() {
    const api = new DaemonStatusApi();
    api.daemonStatusEnvNameGet({ envName: this.envName }).subscribe({
      next: (data: DaemonStatusApiModel[]) => {
        this.setDaemonStatuses(data);
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading daemon statuses')}
    );
  }

  protected firstUpdated(_changedProperties: PropertyValues): void {
    super.firstUpdated(_changedProperties);
    this.addEventListener(
      'daemon-status-changed',
      this.daemonStatusUpdated as EventListener
    );
  }

  daemonStatusUpdated(event: CustomEvent<DaemonStatusApiModel>)
  {
    const daemonData = event.detail as DaemonStatusApiModel;
    const index = this.daemonsAndStatuses?.findIndex(
      (daemon) => daemon.DaemonName === daemonData.DaemonName && daemon.ServerName === daemonData.ServerName
    );
    if (index !== undefined && index > -1) {
      const updatedDaemons = [...this.daemonsAndStatuses!];
      updatedDaemons[index] = daemonData;
      this.daemonsAndStatuses = updatedDaemons;
    }
  }

  private setDaemonStatuses(data: DaemonStatusApiModel[]) {
    this.daemonsAndStatuses = data;
    const event = new CustomEvent('daemons-loaded', {
      detail: {
        message: ''
      }
    });
    this.dispatchEvent(event);
  }
}
