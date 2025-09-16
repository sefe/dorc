import '@vaadin/button';
import '@vaadin/dialog';
import { dialogRenderer } from '@vaadin/dialog/lit';
import '@vaadin/text-area';
import '@vaadin/vertical-layout';
import '@vaadin/horizontal-layout';
import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';
import { css, LitElement } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { TerraformPlanApiModel } from '../apis/dorc-api/models/index';
import { TerraformApi } from '../apis/dorc-api/apis/TerraformApi';

@customElement('terraform-plan-dialog')
export class TerraformPlanDialog extends LitElement {
  @property({ type: Number })
  deploymentResultId: number = 0;

  @property({ type: Boolean })
  opened: boolean = false;

  @state()
  private plan: TerraformPlanApiModel | null = null;

  @state()
  private loading: boolean = false;

  @state()
  private error: string | null = null;

  @state()
  private processing: boolean = false;

  private terraformApi = new TerraformApi();

  static get styles() {
    return css`
      .dialog-content {
        padding: 16px;
        height: 100%;
        display: flex;
        flex-direction: column;
      }

      .plan-header {
        border-bottom: 1px solid var(--lumo-contrast-20pct);
        padding-bottom: 16px;
        margin-bottom: 16px;
      }

      .plan-content {
        flex: 1;
        overflow: auto;
        background-color: var(--lumo-contrast-5pct);
        border: 1px solid var(--lumo-contrast-20pct);
        border-radius: var(--lumo-border-radius-m);
        padding: 12px;
        margin-bottom: 16px;
      }

      .plan-text {
        font-family: 'Courier New', monospace;
        font-size: var(--lumo-font-size-s);
        white-space: pre-wrap;
        margin: 0;
        line-height: 1.4;
      }

      .actions {
        display: flex;
        gap: 12px;
        justify-content: flex-end;
        padding-top: 16px;
        border-top: 1px solid var(--lumo-contrast-20pct);
      }

      .status-badge {
        display: inline-block;
        padding: 4px 8px;
        border-radius: var(--lumo-border-radius-m);
        font-size: var(--lumo-font-size-s);
        font-weight: 500;
      }

      .status-waiting {
        background-color: var(--lumo-warning-color-10pct);
        color: var(--lumo-warning-text-color);
      }

      .status-confirmed {
        background-color: var(--lumo-success-color-10pct);
        color: var(--lumo-success-text-color);
      }

      .status-running {
        background-color: var(--lumo-primary-color-10pct);
        color: var(--lumo-primary-text-color);
      }

      .error-message {
        color: var(--lumo-error-text-color);
        background-color: var(--lumo-error-color-10pct);
        padding: 12px;
        border-radius: var(--lumo-border-radius-m);
        margin-bottom: 16px;
      }

      .loading-indicator {
        text-align: center;
        padding: 20px;
      }

      vaadin-button[theme~="primary"] {
        background-color: var(--lumo-success-color);
      }

      vaadin-button[theme~="error"] {
        background-color: var(--lumo-error-color);
      }
    `;
  }

  render() {
    return html`
      <vaadin-dialog
        .opened="${this.opened}"
        @opened-changed="${this._onDialogOpenedChanged}"
        header-title="Terraform Plan"
        resizable
        draggable
        modeless
        ${dialogRenderer(this._renderContent, [this.plan, this.error])}
      >
      </vaadin-dialog>
    `;
  }

  private _renderContent = () => {
    if (this.loading) {
      return html`
        <div class="loading-indicator">
          <vaadin-icon icon="vaadin:spinner" style="animation: spin 1s linear infinite;"></vaadin-icon>
          <p>Loading Terraform plan...</p>
        </div>
      `;
    }

    if (this.error) {
      return html`
        <div class="error-message">
          <strong>Error:</strong> ${this.error}
        </div>
        <div class="actions">
          <vaadin-button @click="${this._close}">Close</vaadin-button>
        </div>
      `;
    }

    if (!this.plan) {
      return html`
        <div class="error-message">
          No plan data available.
        </div>
        <div class="actions">
          <vaadin-button @click="${this._close}">Close</vaadin-button>
        </div>
      `;
    }

    return html`
      <div class="plan-header">
        <h3>Deployment Result ID: ${this.plan.DeploymentResultId}</h3>
        <p>
          Created: ${this.plan.CreatedAt ? new Date(this.plan.CreatedAt).toLocaleString() : 'Unknown'}
          <span class="status-badge ${this._getStatusClass(this.plan.Status)}">
            ${this.plan.Status}
          </span>
        </p>
      </div>

      <div class="plan-content">
        <pre class="plan-text">${this.plan.PlanContent || 'No plan content available'}</pre>
      </div>

      <div class="actions">
        ${this._renderActionButtons()}
      </div>
    `;
  }

  private _renderActionButtons() {
    const canConfirm = this.plan?.Status === 'WaitingConfirmation';
    const canDecline = this.plan?.Status === 'WaitingConfirmation';

    return html`
      ${canConfirm ? html`
        <vaadin-button
          theme="primary success"
          @click="${this._confirmPlan}"
          .disabled="${this.processing}"
        >
          <vaadin-icon icon="vaadin:check" slot="prefix"></vaadin-icon>
          Confirm & Execute
        </vaadin-button>
      ` : ''}
      
      ${canDecline ? html`
        <vaadin-button
          theme="error"
          @click="${this._declinePlan}"
          .disabled="${this.processing}"
        >
          <vaadin-icon icon="vaadin:close" slot="prefix"></vaadin-icon>
          Decline
        </vaadin-button>
      ` : ''}
      
      <vaadin-button @click="${this._close}">Close</vaadin-button>
    `;
  }

  private _getStatusClass(status: string | null | undefined): string {
    switch (status) {
      case 'WaitingConfirmation':
        return 'status-waiting';
      case 'Confirmed':
        return 'status-confirmed';
      case 'Running':
        return 'status-running';
      default:
        return '';
    }
  }

  private async _onDialogOpenedChanged(e: CustomEvent) {
    this.opened = e.detail.value;
    if (this.opened && this.deploymentResultId > 0) {
      await this._loadPlan();
    }
  }

  private async _loadPlan() {
    this.loading = true;
    this.error = null;
    this.plan = null;

    this.terraformApi.terraformPlanDeploymentResultIdGet({ deploymentResultId: this.deploymentResultId }).subscribe({
      next: (data: TerraformPlanApiModel) => {
        this.plan = data;
        this.loading = false;
      },
      error: (err: any) => {
        console.error(err);
        this.loading = false;
      },
      complete: () => console.log('done loading result Statuses')
    });
  }

  private async _confirmPlan() {
    if (!this.plan) return;

    this.processing = true;      

    this.terraformApi.terraformPlanDeploymentResultIdConfirmPost({ deploymentResultId: this.deploymentResultId }).subscribe({
      error: (err: any) => {
        console.error(err);
      },
      complete: () => console.log('done confirming the Terraform plan')
    });
      
    // Update the plan status
    this.plan = { ...this.plan, Status: 'Confirmed' };
     
    // Dispatch custom event to notify parent component
    this.dispatchEvent(new CustomEvent('terraform-plan-confirmed', {
      detail: { 
        deploymentResultId: this.plan.DeploymentResultId
      },
      bubbles: true,
      composed: true
    }));
    this.processing = false;

    // Close dialog after confirm
    this._close();
  }

  private async _declinePlan() {
    if (!this.plan) return;

    this.processing = true;

    this.terraformApi.terraformPlanDeploymentResultIdDeclinePost({ deploymentResultId: this.deploymentResultId }).subscribe({
      error: (err: any) => {
        console.error(err);
      },
      complete: () => console.log('done confirming the Terraform plan')
    });
    // Update the plan status
    this.plan = { ...this.plan, Status: 'Cancelled' };
      
    // Dispatch custom event to notify parent component
    this.dispatchEvent(new CustomEvent('terraform-plan-declined', {
      detail: { 
        deploymentResultId: this.plan.DeploymentResultId
      },
      bubbles: true,
      composed: true
    }));

    // Close dialog after decline
    this._close();
  }

  private _close() {
    this.opened = false;
    this._sendCloseDialogEvent();
  }

  public open(deploymentResultId: number) {
    this.deploymentResultId = deploymentResultId;
    this.opened = true;
  }

  private _sendCloseDialogEvent(){
    // Dispatch custom event to notify parent component
    this.dispatchEvent(new CustomEvent('close-terraform-plan', {
      detail: { 
        value: false
      },
      bubbles: true,
      composed: true
    }));
  }
}