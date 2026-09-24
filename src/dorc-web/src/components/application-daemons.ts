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
import type { DiscoverDaemonsResult } from '../apis/dorc-api';
import { Notification } from '@vaadin/notification';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';

@customElement('application-daemons')
export class ApplicationDaemons extends LitElement {
  private _envName = '';

  @property({ type: Array })
  daemonsAndStatuses: DaemonStatusApiModel[] | undefined;

  @property({ type: Boolean })
  public userEditable = false;

  @property({ type: String })
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

  _boundDaemonsButtonsRenderer(item: DaemonStatusApiModel) {
    const daemon = item as DaemonStatusApiModel;
    return html`<daemon-controls
      .daemonDetails="${daemon}"
      .userEditable="${this.userEditable}"
    ></daemon-controls>`;
  }

  public loadDaemons() {
    const api = new DaemonStatusApi(dorcApiConfiguration);
    api.daemonStatusEnvNameGet({ envName: this.envName }).subscribe({
      next: (data: DaemonStatusApiModel[]) => {
        this.setDaemonStatuses(data);
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading daemon statuses')
    });
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
      next: result => {
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

          this.dispatchEvent(
            new CustomEvent('daemons-loaded', { detail: { message: '' } })
          );
        }
      },
      error: (err: any) => {
        console.error('Daemon discovery failed:', err);
        Notification.show(
          `Discovery failed: ${retrieveErrorMessage(err, 'Unknown error')}`,
          {
            theme: 'error',
            position: 'bottom-start',
            duration: 5000
          }
        );

        this.dispatchEvent(
          new CustomEvent('daemons-loaded', { detail: { message: '' } })
        );
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

  private getDaemonStatusesFromDiscovery(
    result: DiscoverDaemonsResult
  ): DaemonStatusApiModel[] | undefined {
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

  daemonStatusUpdated(event: CustomEvent<DaemonStatusApiModel>) {
    const daemonData = event.detail as DaemonStatusApiModel;
    const index = this.daemonsAndStatuses?.findIndex(
      daemon =>
        daemon.DaemonName === daemonData.DaemonName &&
        daemon.ServerName === daemonData.ServerName
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
