import '@vaadin/button';
import '@vaadin/checkbox';
import '@vaadin/text-area';
import '@vaadin/text-field';
import { LitElement, css, html, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import {
  TerraformBackendCutoversApi,
  type RefDataApiModel,
  type TerraformBackendCutoverPreviewApiModel,
  type TerraformStateBackendApiModel
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

export const TERRAFORM_BACKEND_CUTOVER_COMPLETED =
  'terraform-backend-cutover-completed';
export const TERRAFORM_BACKEND_CUTOVER_CLOSED =
  'terraform-backend-cutover-closed';

const SHA256_PATTERN = /^[0-9a-fA-F]{64}$/;
const DISPOSITION_MIN = 20;
const DISPOSITION_MAX = 4000;

type BackendField = keyof Pick<
  TerraformStateBackendApiModel,
  'StorageAccount' | 'ContainerName' | 'SubscriptionId' | 'ResourceGroup'
>;

const BACKEND_FIELDS: {
  key: BackendField;
  label: string;
  required: boolean;
}[] = [
  { key: 'StorageAccount', label: 'State storage account', required: true },
  { key: 'ContainerName', label: 'State container', required: true },
  { key: 'SubscriptionId', label: 'State subscription ID', required: true },
  {
    key: 'ResourceGroup',
    label: 'State resource group (optional)',
    required: false
  }
];

/**
 * Admin-only dialog that moves one environment's Terraform state backend
 * through the audited cutover endpoint. The server owns every guard; this
 * component only gathers the operator's attestation and shows the server's
 * verdict verbatim.
 */
@customElement('terraform-backend-cutover')
export class TerraformBackendCutover extends LitElement {
  @property({ type: Number }) projectId = 0;
  @property({ type: Number }) environmentId = 0;
  @property({ type: String }) environmentName = '';

  @state() private preview: TerraformBackendCutoverPreviewApiModel | undefined;
  @state() private loading = true;
  @state() private submitting = false;
  @state() private error = '';
  @state() private step: 'edit' | 'review' = 'edit';
  @state() private backend: TerraformStateBackendApiModel = {};
  @state() private containersEmpty = false;
  @state() private verificationSha256 = '';
  @state() private disposition = '';

  static styles = css`
    :host {
      position: fixed;
      inset: 0;
      z-index: 200;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: var(--lumo-space-m);
      background: rgba(15, 23, 42, 0.48);
    }
    .dialog {
      width: min(880px, 94vw);
      max-height: 86vh;
      background: var(--dorc-bg-primary, #ffffff);
      color: var(--dorc-text-primary);
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-l);
      box-shadow: 0 20px 50px rgba(0, 0, 0, 0.28);
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }
    header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--lumo-space-m);
      padding: var(--lumo-space-m) var(--lumo-space-l);
      border-bottom: 1px solid var(--dorc-border-color);
    }
    header h2 {
      margin: 0;
      font-size: var(--lumo-font-size-l);
    }
    header p {
      margin: 2px 0 0;
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary);
    }
    .body {
      flex: 1;
      min-height: 0;
      overflow: auto;
      padding: var(--lumo-space-m) var(--lumo-space-l);
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-m);
      font-size: var(--lumo-font-size-s);
    }
    .body h3 {
      margin: 0 0 var(--lumo-space-xs);
      font-size: var(--lumo-font-size-m);
    }
    .body p {
      margin: 0;
    }
    .fields {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--lumo-space-s) var(--lumo-space-m);
    }
    vaadin-text-field,
    vaadin-text-area {
      width: 100%;
      min-width: 0;
    }
    .blockers,
    .error {
      padding: var(--lumo-space-s) var(--lumo-space-m);
      border: 1px solid var(--lumo-error-color-50pct);
      border-left: 3px solid var(--lumo-error-color);
      border-radius: var(--lumo-border-radius-s);
      background: var(--lumo-error-color-10pct);
    }
    .blockers ul {
      margin: var(--lumo-space-xs) 0 0;
      padding-left: 1.2em;
    }
    .note {
      padding: var(--lumo-space-s) var(--lumo-space-m);
      border: 1px solid var(--dorc-border-color);
      border-left: 3px solid var(--lumo-primary-color);
      border-radius: var(--lumo-border-radius-s);
      background: var(--dorc-bg-secondary);
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--lumo-font-size-xs);
    }
    th,
    td {
      text-align: left;
      padding: var(--lumo-space-xs) var(--lumo-space-s);
      border-bottom: 1px solid var(--dorc-border-color);
      vertical-align: top;
    }
    th {
      color: var(--dorc-text-secondary-strong);
      font-weight: 600;
    }
    dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: var(--lumo-space-xs) var(--lumo-space-m);
      margin: 0;
    }
    dt {
      color: var(--dorc-text-secondary-strong);
    }
    dd {
      margin: 0;
      font-family: var(--lumo-font-family-monospace, monospace);
      word-break: break-all;
    }
    footer {
      display: flex;
      justify-content: flex-end;
      gap: var(--lumo-space-s);
      padding: var(--lumo-space-s) var(--lumo-space-l);
      border-top: 1px solid var(--dorc-border-color);
      background: var(--dorc-bg-secondary);
    }
  `;

  connectedCallback() {
    super.connectedCallback();
    this.loadPreview();
  }

  private loadPreview() {
    this.loading = true;
    this.error = '';
    new TerraformBackendCutoversApi(dorcApiConfiguration)
      .projectsProjectIdTerraformStateBackendCutoversPreviewGet({
        projectId: this.projectId,
        environmentId: this.environmentId
      })
      .subscribe({
        next: preview => {
          this.preview = preview;
          this.backend = {
            EnvironmentId: this.environmentId,
            ResourceGroup: preview.CurrentBackend?.ResourceGroup ?? ''
          };
          this.loading = false;
        },
        error: error => {
          this.error =
            retrieveErrorMessage(error) ||
            'Could not load the backend cutover preview.';
          this.loading = false;
        }
      });
  }

  private get blockers() {
    return this.preview?.Blockers ?? [];
  }

  private get legacyRequests() {
    return this.preview?.LegacyRequests ?? [];
  }

  private get missingBackendFields() {
    return BACKEND_FIELDS.filter(
      field => field.required && !(this.backend[field.key] ?? '').trim()
    ).map(field => field.label);
  }

  private get sameAsCurrent() {
    const current = this.preview?.CurrentBackend;
    return (
      !!current &&
      BACKEND_FIELDS.every(
        field =>
          (current[field.key] ?? '').trim().toLowerCase() ===
          (this.backend[field.key] ?? '').trim().toLowerCase()
      )
    );
  }

  private get dispositionProblem() {
    if (this.legacyRequests.length === 0) return '';
    const length = this.disposition.trim().length;
    if (length < DISPOSITION_MIN)
      return `Describe the state disposition for the legacy requests (${DISPOSITION_MIN}–${DISPOSITION_MAX} characters).`;
    if (length > DISPOSITION_MAX)
      return `State disposition must be at most ${DISPOSITION_MAX} characters.`;
    return '';
  }

  /** Reasons the request cannot be sent yet, in display order. */
  get validationProblems(): string[] {
    const problems: string[] = [];
    if (!this.preview || this.loading) return problems;
    problems.push(...this.blockers);
    const missing = this.missingBackendFields;
    if (missing.length) problems.push(`Required: ${missing.join(', ')}.`);
    if (!missing.length && this.sameAsCurrent)
      problems.push('The new backend is identical to the current backend.');
    if (!this.containersEmpty)
      problems.push(
        'Confirm you have verified both state containers hold no objects for this environment.'
      );
    if (!SHA256_PATTERN.test(this.verificationSha256.trim()))
      problems.push(
        'Enter the SHA-256 of your verification evidence (64 hex characters).'
      );
    const disposition = this.dispositionProblem;
    if (disposition) problems.push(disposition);
    return problems;
  }

  private close(detail?: RefDataApiModel) {
    this.dispatchEvent(
      new CustomEvent(
        detail
          ? TERRAFORM_BACKEND_CUTOVER_COMPLETED
          : TERRAFORM_BACKEND_CUTOVER_CLOSED,
        { detail, bubbles: true, composed: true }
      )
    );
  }

  private submit() {
    if (!this.preview || this.submitting || this.validationProblems.length)
      return;
    this.submitting = true;
    this.error = '';
    const clean = (value: string | null | undefined) => (value ?? '').trim();
    new TerraformBackendCutoversApi(dorcApiConfiguration)
      .projectsProjectIdTerraformStateBackendCutoversPost({
        projectId: this.projectId,
        terraformBackendCutoverApiModel: {
          Revision: this.preview.Revision,
          EnvironmentId: this.environmentId,
          ExpectedBackend: this.preview.CurrentBackend,
          Backend: {
            EnvironmentId: this.environmentId,
            StorageAccount: clean(this.backend.StorageAccount),
            ContainerName: clean(this.backend.ContainerName),
            SubscriptionId: clean(this.backend.SubscriptionId),
            ResourceGroup: clean(this.backend.ResourceGroup) || null
          },
          ConfirmBothContainersEmpty: this.containersEmpty,
          VerificationSha256: this.verificationSha256.trim().toLowerCase(),
          LegacyRetirement: this.legacyRequests.length
            ? {
                RequestIds: this.legacyRequests
                  .map(request => request.Id)
                  .filter((id): id is number => typeof id === 'number'),
                StateDisposition: this.disposition.trim()
              }
            : undefined
        }
      })
      .subscribe({
        next: document => {
          this.submitting = false;
          this.close(document);
        },
        error: error => {
          this.submitting = false;
          this.step = 'edit';
          this.error =
            retrieveErrorMessage(error) || 'The backend cutover was rejected.';
        }
      });
  }

  private renderBackend(backend: TerraformStateBackendApiModel | undefined) {
    if (!backend) return html`<p>No backend recorded.</p>`;
    return html`<dl>
      ${BACKEND_FIELDS.map(
        field =>
          html`<dt>${field.label.replace(' (optional)', '')}</dt>
            <dd>${backend[field.key] || '—'}</dd>`
      )}
    </dl>`;
  }

  private renderLegacyRequests() {
    if (!this.legacyRequests.length) return nothing;
    return html`<section>
      <h3>Legacy requests without pinned definitions</h3>
      <p>
        These requests ran before definitions were pinned, so DOrc cannot prove
        which backend they used. The cutover retires them; record what happens
        to any state they left behind.
      </p>
      <table>
        <thead>
          <tr>
            <th>Request</th>
            <th>Status</th>
            <th>Requested by</th>
            <th>Requested</th>
            <th>Components</th>
          </tr>
        </thead>
        <tbody>
          ${this.legacyRequests.map(
            request =>
              html`<tr>
                <td>${request.Id}</td>
                <td>${request.Status ?? ''}</td>
                <td>${request.UserName ?? ''}</td>
                <td>
                  ${
                    request.RequestedTime
                      ? new Date(request.RequestedTime).toLocaleString()
                      : ''
                  }
                </td>
                <td>${request.Components ?? ''}</td>
              </tr>`
          )}
        </tbody>
      </table>
      ${
        this.step === 'edit'
          ? html`<vaadin-text-area
              label="State disposition"
              helper-text="Where did any state for these requests go? 20–4000 characters. Recorded against the change."
              .value=${this.disposition}
              .disabled=${this.submitting}
              @input=${(event: Event) => {
                this.disposition = (
                  event.currentTarget as HTMLTextAreaElement
                ).value;
              }}
            ></vaadin-text-area>`
          : html`<p class="note">${this.disposition.trim()}</p>`
      }
    </section>`;
  }

  private renderEdit() {
    return html`
      ${
        this.blockers.length
          ? html`<div class="blockers" role="alert">
              <strong>This environment cannot be cut over yet.</strong>
              <ul>
                ${this.blockers.map(blocker => html`<li>${blocker}</li>`)}
              </ul>
            </div>`
          : nothing
      }
      <section>
        <h3>Current backend</h3>
        ${this.renderBackend(this.preview?.CurrentBackend)}
        <p>
          ${this.preview?.PinnedRequestCount ?? 0} request(s) are pinned to this
          backend and will be marked as retired by this change.
        </p>
      </section>
      <section>
        <h3>New backend</h3>
        <div class="fields">
          ${BACKEND_FIELDS.map(
            field =>
              html`<vaadin-text-field
                label=${field.label}
                data-field=${field.key}
                .value=${this.backend[field.key] ?? ''}
                .disabled=${this.submitting}
                ?required=${field.required}
                @input=${(event: Event) => {
                  this.backend = {
                    ...this.backend,
                    [field.key]: (event.currentTarget as HTMLInputElement).value
                  };
                }}
              ></vaadin-text-field>`
          )}
        </div>
      </section>
      ${this.renderLegacyRequests()}
      <section>
        <h3>Operator attestation</h3>
        <p>
          DOrc cannot read storage containers. You must verify, outside DOrc,
          that neither the current nor the new container holds state for this
          environment, and keep that evidence.
        </p>
        <vaadin-checkbox
          label="I have verified both containers hold no state objects for this environment."
          .checked=${this.containersEmpty}
          .disabled=${this.submitting}
          @checked-changed=${(event: CustomEvent<{ value: boolean }>) => {
            this.containersEmpty = event.detail.value;
          }}
        ></vaadin-checkbox>
        <vaadin-text-field
          label="Verification evidence SHA-256"
          helper-text="Hash of the saved verification record (for example the Azure CLI listing of both containers)."
          data-field="VerificationSha256"
          .value=${this.verificationSha256}
          .disabled=${this.submitting}
          required
          @input=${(event: Event) => {
            this.verificationSha256 = (
              event.currentTarget as HTMLInputElement
            ).value;
          }}
        ></vaadin-text-field>
      </section>
    `;
  }

  private renderReview() {
    return html`
      <p class="note">
        Review the change. Submitting records an audited
        <code>TerraformBackend.LegacyCutover</code> change against this project
        and cannot be undone from the UI.
      </p>
      <section>
        <h3>From</h3>
        ${this.renderBackend(this.preview?.CurrentBackend)}
      </section>
      <section>
        <h3>To</h3>
        ${this.renderBackend(this.backend)}
      </section>
      ${this.renderLegacyRequests()}
      <section>
        <h3>Attestation</h3>
        <dl>
          <dt>Both containers empty</dt>
          <dd>Confirmed</dd>
          <dt>Evidence SHA-256</dt>
          <dd>${this.verificationSha256.trim().toLowerCase()}</dd>
          <dt>Document revision</dt>
          <dd>${this.preview?.Revision ?? ''}</dd>
        </dl>
      </section>
    `;
  }

  render() {
    const problems = this.validationProblems;
    return html`<div
      class="dialog"
      role="dialog"
      aria-modal="true"
      aria-labelledby="cutover-title"
    >
      <header>
        <div>
          <h2 id="cutover-title">
            Migrate Terraform state backend —
            ${this.preview?.EnvironmentName || this.environmentName}
          </h2>
          <p>
            Admin only. Moves the backend for one environment after an
            out-of-band state verification.
          </p>
        </div>
        <vaadin-button
          theme="tertiary"
          .disabled=${this.submitting}
          @click=${() => this.close()}
          >Close</vaadin-button
        >
      </header>
      <div class="body">
        ${
          this.error
            ? html`<div class="error" role="alert">${this.error}</div>`
            : nothing
        }
        ${
          this.loading
            ? html`<p>Loading cutover preview…</p>`
            : this.preview
              ? this.step === 'edit'
                ? this.renderEdit()
                : this.renderReview()
              : nothing
        }
      </div>
      <footer>
        ${
          this.step === 'review'
            ? html`<vaadin-button
                  .disabled=${this.submitting}
                  @click=${() => {
                    this.step = 'edit';
                  }}
                  >Back</vaadin-button
                >
                <vaadin-button
                  theme="primary error"
                  class="submit"
                  .disabled=${this.submitting || problems.length > 0}
                  @click=${this.submit}
                  >${this.submitting ? 'Submitting…' : 'Submit cutover'}</vaadin-button
                >`
            : html`<vaadin-button
                theme="primary"
                class="review"
                title=${problems[0] ?? ''}
                .disabled=${this.loading || !this.preview || problems.length > 0}
                @click=${() => {
                  this.step = 'review';
                }}
                >Review change</vaadin-button
              >`
        }
      </footer>
    </div>`;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'terraform-backend-cutover': TerraformBackendCutover;
  }
}
