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
import {
  TerraformPlanApiModel,
  TerraformInventoryProvenanceApiModel
} from '../apis/dorc-api/models/index';
import { TerraformApi } from '../apis/dorc-api/apis/TerraformApi';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';

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

  private terraformApi = new TerraformApi(dorcApiConfiguration);
  private loadToken = 0;
  @state() private inventory: TerraformInventoryProvenanceApiModel[] | null =
    null;

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

      /* No display: block - we rely on the parent <pre> preserving
         the trailing newline inside each span so each line gets its
         own colour band without double-spacing. */

      /* Colour contrast targets WCAG AA (>= 4.5:1) against the lumo
         contrast-5pct background used by .plan-content. Lumo's success/
         error/warning text colours are tuned for that ratio. */
      .plan-line-add {
        color: var(--lumo-success-text-color);
        background-color: var(--lumo-success-color-10pct);
      }

      .plan-line-destroy {
        color: var(--lumo-error-text-color);
        background-color: var(--lumo-error-color-10pct);
      }

      .plan-line-change {
        color: var(--lumo-warning-text-color);
        background-color: var(--lumo-warning-color-10pct);
      }

      .plan-line-replace {
        color: var(--lumo-error-text-color);
        background-color: var(--lumo-warning-color-10pct);
        font-weight: 600;
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

      @keyframes spin {
        from {
          transform: rotate(0deg);
        }
        to {
          transform: rotate(360deg);
        }
      }

      .plan-summary {
        display: flex;
        gap: 8px;
        flex-wrap: wrap;
        margin-bottom: 12px;
      }

      .plan-summary__chip {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        padding: 4px 12px;
        border-radius: 999px;
        font-size: var(--lumo-font-size-s);
        font-weight: 600;
      }

      .plan-summary__chip strong {
        font-size: var(--lumo-font-size-m);
      }

      .plan-summary__chip.add {
        background: var(--lumo-success-color-10pct);
        color: var(--lumo-success-text-color);
      }

      .plan-summary__chip.change {
        background: var(--lumo-warning-color-10pct);
        color: var(--lumo-warning-text-color);
      }

      .plan-summary__chip.destroy {
        background: var(--lumo-error-color-10pct);
        color: var(--lumo-error-text-color);
      }

      .plan-summary__chip.none {
        background: var(--lumo-contrast-5pct);
        color: var(--lumo-secondary-text-color);
      }

      vaadin-button[theme~='primary'] {
        background-color: var(--lumo-success-color);
        color: var(--lumo-success-contrast-color, #fff);
      }

      vaadin-button[theme~='error'] {
        background-color: var(--lumo-error-color);
        color: var(--lumo-error-contrast-color, #fff);
      }
    `;
  }

  render() {
    return html`
      <vaadin-dialog
        .opened="${this.opened}"
        .noCloseOnEsc=${this.processing}
        .noCloseOnOutsideClick=${this.processing}
        @opened-changed="${this._onDialogOpenedChanged}"
        header-title="Terraform Plan"
        theme="wide"
        resizable
        draggable
        modeless
        ${dialogRenderer(this._renderContent, [
          this.plan,
          this.error,
          this.loading,
          this.processing,
          this.inventory
        ])}
      >
      </vaadin-dialog>
    `;
  }

  private _renderContent = () => {
    if (this.loading) {
      return html`
        <div class="loading-indicator">
          <vaadin-icon
            icon="vaadin:spinner"
            style="animation: spin 1s linear infinite;"
          ></vaadin-icon>
          <p>Loading Terraform plan...</p>
        </div>
      `;
    }

    if (this.error && !this.plan) {
      return html`
        <div class="error-message" role="alert">
          <strong>Error:</strong> ${this.error}
        </div>
        <div class="actions">
          <vaadin-button @click="${this._close}">Close</vaadin-button>
        </div>
      `;
    }

    if (!this.plan) {
      return html`
        <div class="error-message">No plan data available.</div>
        <div class="actions">
          <vaadin-button @click="${this._close}">Close</vaadin-button>
        </div>
      `;
    }

    return html`
      ${this.error ? html`<div class="error-message" role="alert">${this.error}</div>` : ''}
      <div class="plan-header">
        <h3>Deployment Result ID: ${this.plan.DeploymentResultId}</h3>
        ${this.plan.IsHistorical ? html`<p>Archived attempt — read-only</p>` : ''}
        ${
          this.plan.Receipt
            ? html` <dl aria-label="Deployment receipt">
                <dt>Target</dt>
                <dd>
                  ${this.plan.Receipt.ProjectName} /
                  ${this.plan.Receipt.EnvironmentName} /
                  ${this.plan.Receipt.ComponentName}
                </dd>
                <dt>Planned module</dt>
                <dd>
                  ${this.plan.Receipt.TemplateName || 'Non-catalog source'} @
                  ${this.plan.Receipt.TemplateVersion || 'Unavailable'}
                </dd>
                <dt>Source commit</dt>
                <dd>${this.plan.Receipt.GitCommitSha || 'Unavailable'}</dd>
                <dt>Planned workload target</dt>
                <dd>
                  ${this.plan.Receipt.WorkloadTarget || 'Unavailable; not inferred from current properties'}
                </dd>
                <dt>State</dt>
                <dd>
                  ${this.plan.Receipt.StateStorageAccount || 'Unavailable'} /
                  ${this.plan.Receipt.StateContainerName || 'Unavailable'} /
                  ${this.plan.Receipt.StateKey || 'Unavailable'}
                </dd>
              </dl>`
            : html`<p>
                Plan-time deployment receipt unavailable for this record.
                Current component settings are not historical evidence.
              </p>`
        }
        <p>
          Approval:
          ${
            this.plan.ApprovedBy
              ? `${this.plan.ApprovedBy} — ${this.plan.ApprovedAt ? new Date(this.plan.ApprovedAt).toLocaleString() : 'Time unavailable'}`
              : 'No retained approval'
          }
        </p>
        <p>
          Created:
          ${this.plan.CreatedAt ? new Date(this.plan.CreatedAt).toLocaleString() : 'Unknown'}
          <span class="status-badge ${this._getStatusClass(this.plan.Status)}">
            ${this.plan.Status}
          </span>
        </p>
      </div>

      ${this._renderOutcomes()} ${this._renderPlanSummary()}
      <div class="plan-content">${this._renderPlanBody()}</div>

      <div class="actions">${this._renderActionButtons()}</div>
    `;
  };

  private _renderOutcomes() {
    const outcome = this.plan?.Outcome;
    return html`<section aria-label="Post-apply outcomes">
      <h4>Infrastructure and post-apply metadata</h4>
      ${
        outcome
          ? html`
              <p>
                Terraform apply: ${outcome.ApplyOutcome}. Inventory
                registration: ${outcome.InventoryOutcome}
                (${outcome.RegisteredCount ?? 0} registered;
                ${outcome.FailedCount ?? 0} failures). Audit source copy:
                ${outcome.ArchiveOutcome}.
              </p>
              ${(outcome.InventoryWarnings ?? []).map(warning => html`<p>${warning}</p>`)}
              ${
                outcome.ApplyOutcome === 'Succeeded'
                  ? html`
                      <p>
                        Metadata recovery requires an administrator. It never
                        re-runs Terraform apply, imports, destroys or changes
                        the approved plan.
                      </p>
                      ${
                        outcome.InventoryOutcome !== 'Succeeded'
                          ? html`
                              <vaadin-button
                                .disabled=${this.processing || !outcome.InventoryRecoveryAvailable}
                                @click=${() => this._recoverMetadata(true)}
                                >Retry inventory registration
                                only</vaadin-button
                              >
                              ${!outcome.InventoryRecoveryAvailable ? html`<p>Retained inventory is unavailable; review registration manually.</p>` : ''}
                            `
                          : ''
                      }
                      ${
                        outcome.ArchiveOutcome !== 'Succeeded'
                          ? html`<vaadin-button
                              .disabled=${this.processing}
                              @click=${() => this._recoverMetadata(false)}
                              >Retry audit source copy only</vaadin-button
                            >`
                          : ''
                      }
                    `
                  : ''
              }
            `
          : html`<p>
              No durable apply outcome was recorded. Do not infer a successful
              apply from current inventory.
            </p>`
      }
      ${
        this.plan?.Receipt?.EnvironmentId
          ? html`
              <vaadin-button
                .disabled=${this.processing}
                @click=${() => this._loadInventory()}
              >
                Show current inventory provenance
              </vaadin-button>
              <a
                href=${`/environment/${encodeURIComponent(this.plan.Receipt.EnvironmentName ?? '')}/components/cloud`}
                >Environment Cloud resources</a
              >
            `
          : ''
      }
      ${
        this.inventory !== null
          ? html`
              <p>
                Current component/environment observations, not a historical
                state export. Missing resources are never deleted or detached
                automatically.
              </p>
              ${
                this.inventory.length === 0
                  ? html`<p>No provenance recorded.</p>`
                  : html` <ul>
                      ${this.inventory.map(
                        row =>
                          html`<li>
                            Cloud definition #${row.CloudResourceId}:
                            ${row.ResourceIdentifier} —
                            ${row.ReconciliationState}; first result
                            #${row.FirstSeenResultId}, last observed result
                            #${row.LastSeenResultId} at
                            ${row.LastSeenAt ? new Date(row.LastSeenAt).toLocaleString() : 'unknown'}.
                            ${row.DatabaseId ? html`<a href=${`/environment/${encodeURIComponent(this.plan?.Receipt?.EnvironmentName ?? '')}/components/databases`}>Database #${row.DatabaseId}</a>` : ''}
                            ${
                              row.ReconciliationState ===
                              'MissingFromLatestState'
                                ? html`<vaadin-button
                                    .disabled=${this.processing}
                                    @click=${() => this._reviewInventory(row)}
                                  >
                                    Mark reviewed — metadata only</vaadin-button
                                  >`
                                : ''
                            }
                          </li>`
                      )}
                    </ul>`
              }
            `
          : ''
      }
      <vaadin-button
        .disabled=${this.processing}
        @click=${() => this._loadPlan()}
        >Reload receipt</vaadin-button
      >
    </section>`;
  }

  private _recoverMetadata(inventory: boolean) {
    if (this.processing || !this.plan?.Outcome) return;
    this.processing = true;
    this.error = null;
    this.terraformApi
      .terraformRecoverPost({
        deploymentResultId: this.deploymentResultId,
        expectedRevision: this.plan.Outcome.Revision ?? 0,
        recoverInventory: inventory,
        recoverArchive: !inventory
      })
      .subscribe({
        next: outcome => {
          this.plan = { ...this.plan!, Outcome: outcome };
          this.processing = false;
        },
        error: error => {
          this.error =
            retrieveErrorMessage(error) ??
            'Recovery was not completed. Reload the receipt and review retained artifacts.';
          this.processing = false;
        }
      });
  }

  private _loadInventory() {
    if (this.processing) return;
    this.processing = true;
    this.error = null;
    this.terraformApi
      .terraformInventoryGet({ deploymentResultId: this.deploymentResultId })
      .subscribe({
        next: inventory => {
          this.inventory = inventory;
          this.processing = false;
        },
        error: error => {
          this.error =
            retrieveErrorMessage(error) ??
            'Inventory provenance is unavailable.';
          this.processing = false;
        }
      });
  }

  private _reviewInventory(row: TerraformInventoryProvenanceApiModel) {
    if (this.processing || !row.CloudResourceId) return;
    this.processing = true;
    this.error = null;
    this.terraformApi
      .terraformInventoryReviewPost({
        deploymentResultId: this.deploymentResultId,
        cloudResourceId: row.CloudResourceId,
        expectedRevision: row.Revision ?? 0
      })
      .subscribe({
        next: () => {
          this.processing = false;
          this._loadInventory();
        },
        error: error => {
          this.error =
            retrieveErrorMessage(error) ??
            'Review was not saved. Reload inventory.';
          this.processing = false;
        }
      });
  }

  private _renderActionButtons() {
    const canConfirm =
      !this.plan?.IsHistorical && this.plan?.Status === 'WaitingConfirmation';
    const canDecline = canConfirm;

    return html`
      ${
        canConfirm
          ? html`
              <vaadin-button
                theme="primary success"
                @click="${this._confirmPlan}"
                .disabled="${this.processing}"
              >
                <vaadin-icon icon="vaadin:check" slot="prefix"></vaadin-icon>
                Confirm & Apply
              </vaadin-button>
            `
          : ''
      }
      ${
        canDecline
          ? html`
              <vaadin-button
                theme="error"
                @click="${this._declinePlan}"
                .disabled="${this.processing}"
              >
                <vaadin-icon icon="vaadin:close" slot="prefix"></vaadin-icon>
                Decline
              </vaadin-button>
            `
          : ''
      }

      <vaadin-button @click="${this._close}" .disabled=${this.processing}
        >Close</vaadin-button
      >
    `;
  }

  /**
   * Terraform's own one-line verdict ("Plan: 2 to add, 0 to change, 1 to
   * destroy." or "No changes."), lifted out of the plan text so the reader
   * does not have to scroll to the bottom to learn whether anything is
   * destroyed.
   */
  static summarisePlan(
    content: string | null | undefined
  ): { add: number; change: number; destroy: number } | 'none' | null {
    if (!content) return null;
    const match = content.match(
      /Plan:\s*(\d+)\s+to add,\s*(\d+)\s+to change,\s*(\d+)\s+to destroy/
    );
    if (match) {
      return {
        add: Number(match[1]),
        change: Number(match[2]),
        destroy: Number(match[3])
      };
    }
    return /No changes\./.test(content) ? 'none' : null;
  }

  private _renderPlanSummary() {
    const summary = TerraformPlanDialog.summarisePlan(this.plan?.PlanContent);
    if (summary === null) return html``;
    if (summary === 'none') {
      return html`<div class="plan-summary" role="status">
        <span class="plan-summary__chip none">
          <vaadin-icon
            icon="vaadin:check"
            style="width:14px;height:14px"
          ></vaadin-icon>
          No changes
        </span>
      </div>`;
    }
    const chip = (cls: string, count: number, label: string) =>
      html`<span class="plan-summary__chip ${count ? cls : 'none'}">
        <strong>${count}</strong> ${label}
      </span>`;
    return html`<div
      class="plan-summary"
      role="status"
      aria-label="Plan summary"
    >
      ${chip('add', summary.add, 'to add')}
      ${chip('change', summary.change, 'to change')}
      ${chip('destroy', summary.destroy, 'to destroy')}
    </div>`;
  }

  // The secret-name pattern below mirrors the server-side
  // SensitivePropertyRedactor default so the plan dialog redacts the
  // same shape of values the runner redacts in logs. Inlined in
  // _maskSensitiveValues for a single source of truth at the call site.
  private _renderPlanBody() {
    const content = this.plan?.PlanContent;
    if (!content || content.trim() === '') {
      return html`
        <div class="error-message">
          No plan content available. The plan may have failed to generate; check
          the deployment log for details.
        </div>
      `;
    }

    const lines = content.split(/\r?\n/);
    return html`<pre class="plan-text">
${lines.map(line => {
        const cls = this._classifyPlanLine(line);
        const masked = this._maskSensitiveValues(line);
        return html`<span class="plan-line ${cls}">${`${masked}\n`}</span>`;
      })}</pre>`;
  }

  private _classifyPlanLine(line: string): string {
    // Terraform plan output starts each resource-action line with a
    // leading two-character marker after the indent: '  +', '  -',
    // '  ~', '-/+' for replace. We classify the first non-whitespace
    // marker character.
    const trimmed = line.replace(/^\s+/, '');
    if (trimmed.startsWith('-/+') || trimmed.startsWith('+/-')) {
      return 'plan-line-replace';
    }
    if (trimmed.startsWith('+ ')) {
      return 'plan-line-add';
    }
    if (trimmed.startsWith('- ')) {
      return 'plan-line-destroy';
    }
    if (trimmed.startsWith('~ ')) {
      return 'plan-line-change';
    }
    return '';
  }

  private _maskSensitiveValues(line: string): string {
    // 1) Terraform's own marker for sensitive values: replace verbatim.
    if (line.includes('(sensitive value)')) {
      return line.replace(/=\s*"[^"]*"/g, '= (sensitive value)');
    }
    // 2) Heuristic: redact the right-hand side of a `key = value` assignment
    //    where the key matches the secret pattern. Covers quoted strings,
    //    bare numbers/booleans/identifiers, and heredoc openers (<<-EOT /
    //    <<EOT) so a sensitive-named Number/Bool value or a multi-line
    //    heredoc is not shown in the clear. Best-effort, single-line: the
    //    body of a heredoc block is not redacted here, only its opener.
    return line.replace(
      /(["']?)([A-Za-z0-9_.-]*?(token|pat|secret|password|key|connectionstring)[A-Za-z0-9_.-]*?)\1(\s*=\s*)(".*?"|<<-?[A-Za-z0-9_]+|[^\s#]+)/gi,
      (_match, q, name, _kw, eq) => `${q}${name}${q}${eq}[REDACTED]`
    );
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
    const token = ++this.loadToken;
    this.loading = true;
    this.error = null;
    this.plan = null;
    this.inventory = null;

    this.terraformApi
      .terraformPlanDeploymentResultIdGet({
        deploymentResultId: this.deploymentResultId
      })
      .subscribe({
        next: (data: TerraformPlanApiModel) => {
          if (token !== this.loadToken || !this.opened) return;
          this.plan = data;
          this.loading = false;
        },
        error: (err: any) => {
          if (token !== this.loadToken || !this.opened) return;
          this.error =
            retrieveErrorMessage(err) ?? 'Could not load the Terraform plan.';
          this.loading = false;
        },
        complete: () => console.log('done loading result Statuses')
      });
  }

  private _confirmPlan() {
    this._submitDecision(true);
  }

  private _declinePlan() {
    this._submitDecision(false);
  }

  private _submitDecision(confirm: boolean) {
    if (
      !this.plan ||
      this.plan.Status !== 'WaitingConfirmation' ||
      this.processing
    )
      return;
    const plan = this.plan;
    const deploymentResultId = this.deploymentResultId;
    this.processing = true;
    this.error = null;
    const operation = confirm
      ? this.terraformApi.terraformPlanDeploymentResultIdConfirmPost({
          deploymentResultId,
          planContentSha256: plan.PlanContentSha256
        })
      : this.terraformApi.terraformPlanDeploymentResultIdDeclinePost({
          deploymentResultId
        });
    operation.subscribe({
      error: err => {
        this.processing = false;
        this.error =
          retrieveErrorMessage(err) ??
          `Could not ${confirm ? 'confirm' : 'decline'} the plan.`;
      },
      complete: () => {
        this.plan = { ...plan, Status: confirm ? 'Confirmed' : 'Cancelled' };
        this.processing = false;
        this.dispatchEvent(
          new CustomEvent(
            confirm ? 'terraform-plan-confirmed' : 'terraform-plan-declined',
            {
              detail: { deploymentResultId },
              bubbles: true,
              composed: true
            }
          )
        );
        this._close();
      }
    });
  }

  private _close() {
    if (this.processing) return;
    this.loadToken += 1;
    this.opened = false;
    this._sendCloseDialogEvent();
  }

  public open(deploymentResultId: number) {
    if (this.processing) return;
    this.deploymentResultId = deploymentResultId;
    if (this.opened) void this._loadPlan();
    this.opened = true;
  }

  private _sendCloseDialogEvent() {
    // Dispatch custom event to notify parent component
    this.dispatchEvent(
      new CustomEvent('close-terraform-plan', {
        detail: {
          value: false
        },
        bubbles: true,
        composed: true
      })
    );
  }
}
