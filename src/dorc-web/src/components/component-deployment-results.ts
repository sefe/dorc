import '@polymer/paper-dialog';
import '@vaadin/button';
import '@vaadin/grid';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, LitElement, render } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import './grid-button-groups/server-controls';
import './log-dialog';
import './grid-button-groups/database-env-controls.ts';
import '../components/server-tags';
import { DeploymentResultApiModel, ResultStatusesApi } from '../apis/dorc-api';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';

@customElement('component-deployment-results')
export class ComponentDeploymentResults extends LitElement {
  @property({ type: Array })
  resultItems: DeploymentResultApiModel[] | undefined;

  @state()
  dialogOpened = false;

  @state()
  selectedLog: string | undefined;

  @state()
  isLoadingLog = false;

  constructor() {
    super();

    this.addEventListener('open-result-log', this.viewLog as EventListener);

    this.addEventListener(
      'log-dialog-closed',
      this.logDialogClosed as EventListener
    );
  }

  static get styles() {
    return css`
      vaadin-grid#grid {
        overflow: auto;
        width: calc(100% - 4px);
        height: calc(100vh - 410px);
        --divider-color: rgb(223, 232, 239);
      }
      vaadin-grid-cell-content {
        padding-top: 0px;
        padding-bottom: 0px;
        margin: 0px;
      }

      vaadin-text-field {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    return html`
      <log-dialog
        .isOpened="${this.dialogOpened}"
        .selectedLog="${this.selectedLog}"
        .isLoading="${this.isLoadingLog}"
      >
      </log-dialog>

      <vaadin-grid
        id="grid"
        column-reordering-allowed
        multi-sort
        theme="compact row-stripes no-row-borders no-border"
        .items="${this.resultItems}"
        all-rows-visible
      >
        <vaadin-grid-column
          .renderer="${this.componentNameRenderer}"
          header="Component Name"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          resizable
          .renderer="${this.timingsRenderer}"
          header="Timings"
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Status"
          header="Status"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Log"
          header="Log"
          resizable
          auto-width
          .renderer="${this._logRenderer}"
        ></vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  componentNameRenderer(    root: HTMLElement,
                            _column: GridColumn,
                            model: GridItemModel<DeploymentResultApiModel>){

    const result = model.item as DeploymentResultApiModel;
    render(html` <a href="scripts?search-name=${result.ComponentName}" target="_blank">${result.ComponentName}</a> `, root);
  }

  _logRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DeploymentResultApiModel>
  ) {
    const result = model.item as DeploymentResultApiModel;
    const first100chars = result.Log?.substring(0, 100);

    const lines = first100chars?.split(/\r?\n/);
    render(
      html` <table>
        <tr>
          <td>
            <vaadin-button
              theme="small"
              style="width: 36px; min-width: 36px; padding: 0"
              @click="${() =>
                this.dispatchEvent(
                  new CustomEvent('open-result-log', {
                    detail: {
                      result
                    },
                    bubbles: true,
                    composed: true
                  })
                )}"
            >
              <vaadin-icon
                icon="vaadin:ellipsis-dots-h"
                style="color: cornflowerblue"
              ></vaadin-icon>
            </vaadin-button>
          </td>
          <td>
            <div style="font-family: monospace">
              ${lines?.map(
                element =>
                  html`<div
                    style="font-size: var(--lumo-font-size-xs); color: var(--lumo-secondary-text-color);"
                  >
                    ${element}
                  </div>`
              )}
            </div>
          </td>
        </tr>
      </table>`,
      root
    );
  }

  private async viewLog(e: Event) {
    const customEvent = e as CustomEvent;
    const result = customEvent.detail.result as DeploymentResultApiModel;
    
    // Show dialog immediately with loading state
    this.isLoadingLog = true;
    this.selectedLog = '';
    this.dialogOpened = true;
    
    if (result.RequestId) {
      try {
        const api = new ResultStatusesApi();
        const logObservable = api.resultStatusesLogGet({ requestId: result.RequestId, resultId: result.Id });
        
        logObservable.subscribe({
          next: (fullLog: string) => {
            this.selectedLog = fullLog;
            this.isLoadingLog = false;
          },
          error: (error) => {
            console.error('Failed to fetch log:', error);
            // Fallback to the existing log if API call fails
            this.selectedLog = result.Log ?? 'Failed to load full log';
            this.isLoadingLog = false;
          }
        });
      } catch (error) {
        console.error('Failed to create API instance:', error);
        // Fallback to the existing log if API creation fails
        this.selectedLog = result.Log ?? 'Failed to load full log';
        this.isLoadingLog = false;
      }
    } else {
      // Fallback to the existing log if no RequestId
      this.selectedLog = result.Log ?? 'No log available';
      this.isLoadingLog = false;
    }
  }

  private logDialogClosed() {
    this.dialogOpened = false;
    this.isLoadingLog = false;
  }

  private timingsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentResultApiModel>
  ) => {
    const request = model.item as DeploymentResultApiModel;
    let sTime = '';
    let sDate = '';
    let cTime = '';
    let cDate = '';

    if (request.StartedTime !== undefined && request.StartedTime !== null) {
      sTime = new Date(request.StartedTime ?? '')?.toLocaleTimeString('en-GB');
      sDate = new Date(request.StartedTime ?? '')?.toLocaleDateString('en-GB', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric'
      });
    }
    if (request.CompletedTime !== undefined && request.CompletedTime !== null) {
      cTime = new Date(request.CompletedTime ?? '')?.toLocaleTimeString(
        'en-GB'
      );
      cDate = new Date(request.CompletedTime ?? '')?.toLocaleDateString(
        'en-GB',
        {
          day: '2-digit',
          month: '2-digit',
          year: 'numeric'
        }
      );
    }

    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing">
          <vaadin-vertical-layout
            style="line-height: var(--lumo-line-height-s);"
          >
            <div
              style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
            >
              ${`${sDate} ${sTime}`}
            </div>
            <div
              style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
            >
              ${`${cDate} ${cTime}`}
            </div>
          </vaadin-vertical-layout>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };
}
