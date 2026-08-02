import '@polymer/paper-dialog';
import '@vaadin/button';
import '@vaadin/grid';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid-sorter';
import { css, LitElement, render } from 'lit';
import { NarrowListController } from '../helpers/narrow-list-controller';
import {
  listRowStyles,
  renderListBar,
  renderListRow
} from './dorc-list-row';
import { deploymentResultRowTemplate } from '../row-templates/deployment-result-row-template';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import './grid-button-groups/server-controls';
import './log-dialog';
import './grid-button-groups/database-env-controls.ts';
import '../components/server-tags';
import './terraform-plan-dialog';
import { DeploymentResultApiModel, ResultStatusesApi } from '../apis/dorc-api';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';

@customElement('component-deployment-results')
export class ComponentDeploymentResults extends LitElement {
  @property({ type: Array })
  resultItems: DeploymentResultApiModel[] | undefined;

  /** Narrow-mode (HLPS §3.4): container-driven — this component is hosted
   * inside <vaadin-details>, so its real width is the host panel's. */
  list = new NarrowListController(this);

  private rowTemplate = deploymentResultRowTemplate({
    dispatchEvent: e => this.dispatchEvent(e),
    openTerraformPlan: id => this.openTerraformPlan(id)
  });

  private _openedNarrowItems: DeploymentResultApiModel[] = [];

  @state()
  dialogOpened = false;

  @state()
  selectedLog: string | undefined;

  @state()
  isLoadingLog = false;

  @state()
  terraformDialogOpened = false;

  @state()
  selectedTerraformDeploymentId: number = 0;

  constructor() {
    super();

    this.addEventListener('open-result-log', this.viewLog as EventListener);

    this.addEventListener(
      'log-dialog-closed',
      this.logDialogClosed as EventListener
    );

    this.addEventListener('open-terraform-plan', this.viewTerraformPlan as EventListener);

    this.addEventListener(
      'terraform-plan-confirmed',
      this.onTerraformPlanConfirmed as EventListener
    );

    this.addEventListener(
      'terraform-plan-declined',
      this.onTerraformPlanDeclined as EventListener
    );

    this.addEventListener(
      'close-terraform-plan',
      this.onCloseTerraformPlan as EventListener
    );
  }

  static get styles() {
    return [
      listRowStyles,
      css`
      vaadin-grid#grid {
        overflow: auto;
        width: calc(100% - 4px);
        flex: 1;
        min-height: 150px;
        --divider-color: var(--dorc-border-color);
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

      .status-badge {
        display: inline-block;
        padding: 2px 6px;
        border-radius: 4px;
        font-size: var(--lumo-font-size-xs);
        font-weight: 500;
        text-transform: uppercase;
      }

      .status-waiting-confirmation {
        background-color: var(--lumo-warning-color-10pct);
        color: var(--lumo-warning-text-color);
      }

      .status-confirmed {
        background-color: var(--lumo-success-color-10pct);
        color: var(--lumo-success-text-color);
      }

      .status-cancelled {
        background-color: var(--lumo-shade-10pct);
        color: var(--lumo-secondary-text-color);
      }

      .status-failed {
        background-color: var(--lumo-error-color-10pct);
        color: var(--lumo-error-text-color);
      }

      .terraform-actions {
        display: flex;
        gap: 4px;
        align-items: center;
      }

      .terraform-button {
        min-width: 32px;
        padding: 4px;
      }
      @media (max-width: 768px) {
        vaadin-grid-cell-content {
          white-space: normal;
          word-wrap: break-word;
          overflow-wrap: break-word;
        }
      }
    `
    ];
  }

  render() {
    return html`
      <log-dialog
        .isOpened="${this.dialogOpened}"
        .selectedLog="${this.selectedLog}"
        .isLoading="${this.isLoadingLog}"
      >
      </log-dialog>

      <terraform-plan-dialog
        .deploymentResultId="${this.selectedTerraformDeploymentId}"
        .opened="${this.terraformDialogOpened}"
      >
      </terraform-plan-dialog>

      <vaadin-grid
        id="grid"
        column-reordering-allowed
        multi-sort
        all-rows-visible
        theme="compact row-stripes no-row-borders no-border"
        .items="${this.resultItems}"
        .rowDetailsRenderer=${this.list.narrow
          ? this._listDetailsRenderer
          : undefined}
        .detailsOpenedItems=${this._openedNarrowItems}
        @active-item-changed=${this._onNarrowActiveItem}
      >
        <vaadin-grid-column
          flex-grow="1"
          ?hidden="${!this.list.narrow}"
          .headerRenderer="${this._listBarRenderer}"
          .renderer="${this._listRowRenderer}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this.componentNameRenderer}"
          header="Component Name"
          resizable
          auto-width
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          resizable
          .renderer="${this.timingsRenderer}"
          header="Timings"
          auto-width
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this.statusRenderer}"
          header="Status"
          resizable
          auto-width
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this.actionsRenderer}"
          header="Actions"
          resizable
          auto-width
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Log"
          header="Log"
          resizable
          auto-width
          .renderer="${this._logRenderer}"
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  // ---- Narrow-mode list rendering (HLPS §3.4) ----

  private _listRowRenderer = (
    root: HTMLElement,
    _: GridColumn,
    model: GridItemModel<DeploymentResultApiModel>
  ) => {
    render(renderListRow(this.rowTemplate, model.item), root);
  };

  private _listDetailsRenderer = (
    root: HTMLElement,
    _: GridColumn,
    model: GridItemModel<DeploymentResultApiModel>
  ) => {
    render(this.rowTemplate.details!(model.item), root);
  };

  private _onNarrowActiveItem = (e: CustomEvent) => {
    if (!this.list.narrow) return;
    const item = e.detail.value as DeploymentResultApiModel | null;
    if (!item) return;
    const idx = this._openedNarrowItems.indexOf(item);
    this._openedNarrowItems =
      idx === -1
        ? [...this._openedNarrowItems, item]
        : [
            ...this._openedNarrowItems.slice(0, idx),
            ...this._openedNarrowItems.slice(idx + 1)
          ];
    (e.currentTarget as { activeItem?: unknown }).activeItem = null;
    this.requestUpdate();
  };

  /** Sort-only bar; this grid has no filters. */
  private _listBarRenderer = (root: HTMLElement) => {
    render(
      renderListBar({
        sort: html`<vaadin-grid-sorter path="ComponentName"
          >Component</vaadin-grid-sorter
        >`
      }),
      root
    );
  };

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
                icon="vaadin:file-text-o"
                style="color: var(--dorc-link-color)"
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

  statusRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentResultApiModel>
  ) => {
    const result = model.item as DeploymentResultApiModel;
    const status = result.Status || '';
    
    let statusClass = '';
    if (status === 'WaitingConfirmation') {
      statusClass = 'status-waiting-confirmation';
    } else if (status === 'Confirmed') {
      statusClass = 'status-confirmed';
    } else if (status === 'Cancelled') {
      statusClass = 'status-cancelled';
    } else if (status === 'Failed') {
      statusClass = 'status-failed';
    }

    render(
      html`
        <span class="status-badge ${statusClass}">
          ${status}
        </span>
      `,
      root
    );
  };

  actionsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentResultApiModel>
  ) => {
    const result = model.item as DeploymentResultApiModel;
    const status = result.Status || '';
    const isTerraformStatus = status === 'WaitingConfirmation' || status === 'Confirmed';

    if (!isTerraformStatus) {
      render(html`<span>-</span>`, root);
      return;
    }

    render(
      html`
        <div class="terraform-actions">
          <vaadin-button
            class="terraform-button"
            @click="${() => this.openTerraformPlan(result.Id!)}"
            title="View Terraform Plan"
          >
            <vaadin-icon
            icon="vaadin:file-text"
            ></vaadin-icon>
          </vaadin-button>
        </div>
      `,
      root
    );
  };

  private viewTerraformPlan(e: CustomEvent) {
    const deploymentResultId = e.detail.deploymentResultId as number;
    this.openTerraformPlan(deploymentResultId);
  }

  private openTerraformPlan(deploymentResultId: number) {
    this.selectedTerraformDeploymentId = deploymentResultId;
    this.terraformDialogOpened = true;
  }

  private onCloseTerraformPlan(e: CustomEvent) {
    this.terraformDialogOpened = e.detail.value;

    this.dispatchEvent(
      new CustomEvent('refresh-monitor-result', {
        detail: {},
        bubbles: true,
        composed: true
      })
    );
  }

  private onTerraformPlanConfirmed(e: CustomEvent) {
    const deploymentResultId = e.detail.deploymentResultId as number;
    
    // Update the status of the corresponding item in the grid
    if (this.resultItems) {
      const item = this.resultItems.find(item => item.Id === deploymentResultId);
      if (item) {
        item.Status = 'Confirmed';
        this.requestUpdate(); // Force re-render of the grid
      }
    }

    // Dispatch event to notify parent components
    this.dispatchEvent(new CustomEvent('deployment-status-changed', {
      detail: { 
        deploymentResultId,
        newStatus: 'Confirmed'
      },
      bubbles: true,
      composed: true
    }));
  }

  private onTerraformPlanDeclined(e: CustomEvent) {
    const deploymentResultId = e.detail.deploymentResultId as number;
    
    // Update the status of the corresponding item in the grid
    if (this.resultItems) {
      const item = this.resultItems.find(item => item.Id === deploymentResultId);
      if (item) {
        item.Status = 'Cancelled';
        this.requestUpdate(); // Force re-render of the grid
      }
    }

    // Dispatch event to notify parent components
    this.dispatchEvent(new CustomEvent('deployment-status-changed', {
      detail: { 
        deploymentResultId,
        newStatus: 'Cancelled'
      },
      bubbles: true,
      composed: true
    }));
  }
}
