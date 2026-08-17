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
import { DaemonStatusApiModel } from '../apis/dorc-api';
import type { DiscoverDaemonsResult } from '../apis/dorc-api';
import { Notification } from '@vaadin/notification';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

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
    model: GridItemModel<DaemonStatusApiModel>
  ) {
    const daemon = model.item as DaemonStatusApiModel;
    const status = daemon?.Status?.toLowerCase();
    const errorMessage = daemon?.ErrorMessage;
    if (errorMessage) {
      root.style.color = 'var(--dorc-error-color)';
    } else if (status === 'running') {
      root.style.color = 'var(--dorc-success-text)';
    } else if (status === 'stopped') {
      root.style.color = 'var(--dorc-text-primary)';
    } else {
      root.style.color = 'var(--dorc-error-color)';
    }
    if (errorMessage) {
      render(
        html`<span title="${errorMessage}">⚠ ${daemon?.Status ?? 'unreachable'}</span>`,
        root
      );
    } else {
      render(
        html`<span>${daemon?.Status}</span>`,
        root
      );
    }
  }

  _boundDaemonsButtonsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DaemonStatusApiModel>
  ) {
    // The below line has a horrible hack
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const applicationDaemons = _column.attachedAppDaemonControl as ApplicationDaemons;
    const daemon = model.item as DaemonStatusApiModel;
    render(
      html`<daemon-controls .daemonDetails="${daemon}" .userEditable="${applicationDaemons.userEditable}"></daemon-controls>`,
      root
    );
  }

  public loadDaemons() {
    const api = new DaemonStatusApi(dorcApiConfiguration);
    api.daemonStatusEnvNameGet({ envName: this.envName }).subscribe({
      next: (data: DaemonStatusApiModel[]) => {
        this.setDaemonStatuses(data);
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading daemon statuses')}
    );
  }

  public discoverDaemons() {
    if (!this.envName) {
      Notification.show('No environment selected', {
        theme: 'error',
        position: 'bottom-start',
        duration: 3000
      });
      return;
    }

    const api = new DaemonStatusApi(dorcApiConfiguration);
    api.daemonStatusDiscoverEnvNameGet({ envName: this.envName }).subscribe({
      next: (result) => {
        if (result.Success) {
          let message = `Discovery complete: ${result.MappingsCreated} new mapping(s) created`;
          if (result.DaemonsDiscovered && result.DaemonsDiscovered > 0) {
            message += ` (${result.DaemonsDiscovered} daemon(s) discovered)`;
          }

          Notification.show(message, {
            theme: 'success',
            position: 'bottom-start',
            duration: 5000
          });

          if (result.Errors && result.Errors.length > 0) {
            console.warn('Discovery errors:', result.Errors);
          }

          const discoveredDaemons = this.getDaemonStatusesFromDiscovery(result);
          if (discoveredDaemons && discoveredDaemons.length > 0) {
            this.setDaemonStatuses(discoveredDaemons);
          } else {
            // Fallback to reload if no daemons returned
            this.loadDaemons();
          }
        } else {
          Notification.show(
            `Discovery failed: ${result.Errors?.join(', ') || 'Unknown error'}`,
            {
              theme: 'error',
              position: 'bottom-start',
              duration: 5000
            }
          );

          this.dispatchEvent(new CustomEvent('daemons-loaded', { detail: { message: '' } }));
        }
      },
      error: (err: any) => {
        console.error('Daemon discovery failed:', err);
        Notification.show(
          `Discovery failed: ${err?.message || 'Unknown error'}`,
          {
            theme: 'error',
            position: 'bottom-start',
            duration: 5000
          }
        );

        this.dispatchEvent(new CustomEvent('daemons-loaded', { detail: { message: '' } }));
      },
      complete: () => {
        console.log('done discovering daemons');
        const event = new CustomEvent('daemons-loaded', {
          detail: {
            message: ''
          }
        });
        this.dispatchEvent(event);
      }
    });
  }

  private getDaemonStatusesFromDiscovery(result: DiscoverDaemonsResult): DaemonStatusApiModel[] | undefined {
    if (Array.isArray(result.DiscoveredDaemons)) {
      return result.DiscoveredDaemons;
    }

    // Backward-compatible alias in case backend uses a different key temporarily.
    const resultWithAlias = result as DiscoverDaemonsResult & {
      Daemons?: DaemonStatusApiModel[];
    };

    if (Array.isArray(resultWithAlias.Daemons)) {
      return resultWithAlias.Daemons;
    }

    return undefined;
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
