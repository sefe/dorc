import '@vaadin/button';
import '@vaadin/checkbox';
import '@vaadin/dialog';
import {
  dialogFooterRenderer,
  dialogHeaderRenderer,
  dialogRenderer
} from '@vaadin/dialog/lit';
import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';
import { Notification } from '@vaadin/notification';
import '@vaadin/number-field';
import '@vaadin/text-field';
import { css, html, LitElement, nothing, PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type { Subscription } from 'rxjs';
import {
  EnvironmentApiModel,
  EphemeralEnvironmentsApi,
  EphemeralEnvironmentTeardownResponseApiModel,
  RefDataEnvironmentsApi
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { navigate } from '../router/router';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { ErrorNotification } from './notifications/error-notification';
import './notifications/error-notification';

export interface TierProfileBadgeMeta {
  code: string;
  label: string;
  isolationSummary: string;
}

export const TIER_PROFILES_META: Record<string, TierProfileBadgeMeta> = {
  ephemeral: {
    code: 'ephemeral',
    label: 'Ephemeral PR',
    isolationSummary:
      'Shared Host Services (create_service = false, shared Aiven PG/CH, traveler-unstable-dev Kafka 1p/1h, 400 RU/s / Basic SKU)'
  },
  dv: {
    code: 'dv',
    label: 'Dev (dv)',
    isolationSummary:
      'Shared Host Services (create_service = false, shared Aiven PG/CH, traveler-unstable-dev Kafka 1p/1h, 400 RU/s / Basic SKU)'
  },
  qa: {
    code: 'qa',
    label: 'QA (qa)',
    isolationSummary:
      'Dedicated Non-Prod Services (create_service = true, startup-4, traveler-non-prod Kafka 3p/24h, 1000 RU/s / Standard SKU)'
  },
  it: {
    code: 'it',
    label: 'Integration (it)',
    isolationSummary:
      'Dedicated Non-Prod Services (create_service = true, startup-4, traveler-non-prod Kafka 3p/24h, 1000 RU/s / Standard SKU)'
  },
  ut: {
    code: 'ut',
    label: 'UAT (ut)',
    isolationSummary:
      'Dedicated Non-Prod Services (create_service = true, startup-16, traveler-non-prod Kafka 3p/24h, 1000 RU/s / Standard SKU)'
  },
  pp: {
    code: 'pp',
    label: 'Pre-Prod (pp)',
    isolationSummary:
      'Pre-Prod Dedicated Sizing (create_service = true, isolated RG, business-8/16, traveler-non-prod Kafka 6p/7d, 4000 RU/s / Premium SKU)'
  },
  ff: {
    code: 'ff',
    label: 'Fix-on-Fail (ff)',
    isolationSummary:
      'Fix-on-Fail Dedicated Sizing (create_service = true, isolated RG, business-8/16, traveler-non-prod Kafka 6p/7d, 4000 RU/s / Premium SKU)'
  }
};

@customElement('ephemeral-environment-banner')
export class EphemeralEnvironmentBanner extends LitElement {
  @property({ type: Object }) environment: EnvironmentApiModel | undefined;

  @property({ type: Boolean }) compact = false;

  @property({ type: Boolean, attribute: 'full-width', reflect: true })
  fullWidth = false;

  @property({ type: Boolean }) showActions = true;

  @property({ type: Boolean, attribute: 'show-actions-in-compact' })
  showActionsInCompact = false;

  @state() private nowMs = Date.now();

  @state() private extendDialogOpened = false;

  @state() private teardownDialogOpened = false;

  @state() private customExtendMinutes = 60;

  @state() private teardownReason = 'Manual teardown from DOrc Web UI';

  @state() private forceDeleteMetadata = false;

  @state() private isSubmitting = false;

  @state() private lastDestroyRequestId: number | null = null;
  @state() private lastDestroyRequestIds: number[] = [];

  private countdownTimer: number | undefined;

  private statePollTimer: number | undefined;

  private currentPollIntervalMs: number | undefined;

  private statePollSubscription?: Subscription;

  static get styles() {
    return css`
      :host {
        display: inline-block;
        box-sizing: border-box;
      }

      :host([full-width]) {
        display: block;
        width: 100%;
      }

      .banner {
        display: flex;
        align-items: center;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: var(--dorc-space-sm, 8px) var(--dorc-space-md, 12px);
        padding: 6px 12px;
        border-radius: var(--dorc-radius-md, 6px);
        background: var(--lumo-primary-color-10pct);
        border: 1px solid var(--dorc-border-color, rgba(0, 0, 0, 0.12));
        font-size: var(--dorc-font-size-sm, 0.8125rem);
      }

      .banner.banner-warning {
        background: var(--lumo-warning-color-10pct);
        border-color: var(--lumo-warning-color-50pct);
      }

      .banner.banner-danger {
        background: var(--lumo-error-color-10pct);
        border-color: var(--lumo-error-color-50pct);
      }

      .banner.banner-muted {
        background: var(--lumo-contrast-5pct);
        border-color: var(--dorc-border-color, rgba(0, 0, 0, 0.12));
      }

      .badges-group {
        display: inline-flex;
        align-items: center;
        flex-wrap: wrap;
        gap: 6px;
      }

      .compact-strip {
        display: inline-flex;
        align-items: center;
        flex-wrap: wrap;
        gap: 4px 6px;
        vertical-align: middle;
        max-width: 100%;
      }

      .pill {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 2px 8px;
        border-radius: 999px;
        font-size: 0.72rem;
        font-weight: 600;
        line-height: 1.35;
        letter-spacing: 0.02em;
        white-space: nowrap;
      }

      .compact-strip .pill {
        padding: 1px 6px;
        font-size: 0.68rem;
      }

      .pill vaadin-icon {
        width: 12px;
        height: 12px;
      }

      .pill-ephemeral {
        background: #ede9fe;
        color: #5b21b6;
        border: 1px solid #c4b5fd;
      }

      .pill-tier {
        background: var(--lumo-contrast-10pct);
        color: var(--dorc-text-primary, #1f2937);
        border: 1px solid var(--dorc-border-color, rgba(0, 0, 0, 0.12));
        text-transform: uppercase;
      }

      .pill-tier.tier-shared {
        background: #e0f2fe;
        color: #0369a1;
        border-color: #7dd3fc;
      }

      .pill-tier.tier-dedicated {
        background: #fef3c7;
        color: #92400e;
        border-color: #fcd34d;
      }

      .pill-tier.tier-preprod {
        background: #fce7f3;
        color: #9d174d;
        border-color: #f9a8d4;
      }

      .pill-state-provisioning,
      .pill-state-testing {
        background: var(--lumo-primary-color-10pct);
        color: var(--lumo-primary-text-color);
        border: 1px solid var(--lumo-primary-color-50pct);
      }

      .pill-state-ready {
        background: var(--lumo-success-color-10pct);
        color: var(--lumo-success-text-color);
        border: 1px solid var(--lumo-success-color-50pct);
      }

      .pill-state-tearingdown {
        background: var(--lumo-warning-color-10pct);
        color: var(--lumo-warning-text-color);
        border: 1px solid var(--lumo-warning-color-50pct);
      }

      .pill-state-torndown {
        background: var(--lumo-contrast-10pct);
        color: var(--dorc-text-secondary, #6b7280);
        border: 1px solid var(--dorc-border-color, rgba(0, 0, 0, 0.15));
      }

      .pill-state-teardownfailed,
      .pill-state-provisionfailed,
      .pill-state-validationfailed {
        background: var(--lumo-error-color-10pct);
        color: var(--lumo-error-text-color);
        border: 1px solid var(--lumo-error-color-50pct);
      }

      .pill-countdown {
        font-variant-numeric: tabular-nums;
        background: var(--dorc-bg-secondary, #ffffff);
        color: var(--dorc-text-primary, #1f2937);
        border: 1px solid var(--dorc-border-color, rgba(0, 0, 0, 0.15));
      }

      .pill-countdown.countdown-warning {
        background: #fef3c7;
        color: #b45309;
        border-color: #f59e0b;
      }

      .pill-countdown.countdown-danger {
        background: #fee2e2;
        color: #b91c1c;
        border-color: #ef4444;
      }

      .actions-group {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        flex-wrap: wrap;
      }

      .actions-group vaadin-button {
        margin: 0;
      }

      .dialog-content {
        display: flex;
        flex-direction: column;
        gap: 12px;
        width: 100%;
        max-width: 480px;
        min-width: 0;
        box-sizing: border-box;
      }

      .dialog-content vaadin-number-field,
      .dialog-content vaadin-text-field {
        width: 100%;
        min-width: 0;
      }

      .dialog-desc {
        margin: 0;
        font-size: 0.85rem;
        color: var(--dorc-text-secondary, #4b5563);
        line-height: 1.45;
      }

      .preset-buttons {
        display: flex;
        flex-wrap: wrap;
        gap: 8px;
      }

      .preset-btn {
        cursor: pointer;
      }

      .warning-callout {
        padding: 10px 12px;
        border-radius: 6px;
        background: var(--lumo-error-color-10pct);
        border-left: 4px solid var(--lumo-error-color);
        color: var(--dorc-text-primary);
        font-size: 0.82rem;
        line-height: 1.45;
      }

      .dialog-footer {
        display: flex;
        justify-content: flex-end;
        gap: 8px;
        width: 100%;
      }
    `;
  }

  connectedCallback() {
    super.connectedCallback();
    this.startCountdownTimer();
    this.syncStatePolling();
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    this.stopCountdownTimer();
    this.stopStatePolling();
  }

  protected updated(_changedProperties: PropertyValues) {
    super.updated(_changedProperties);
    if (_changedProperties.has('environment')) {
      const prevEnv = _changedProperties.get('environment') as
        EnvironmentApiModel | undefined;
      if (
        prevEnv &&
        (prevEnv.EnvironmentId !== this.environment?.EnvironmentId ||
          prevEnv.EnvironmentName !== this.environment?.EnvironmentName)
      ) {
        this.extendDialogOpened = false;
        this.teardownDialogOpened = false;
        this.isSubmitting = false;
        this.lastDestroyRequestId = null;
        this.lastDestroyRequestIds = [];
        this.statePollSubscription?.unsubscribe();
        this.statePollSubscription = undefined;
      }
    }
    if (
      _changedProperties.has('environment') ||
      _changedProperties.has('compact')
    ) {
      this.syncStatePolling();
    }
  }

  private startCountdownTimer() {
    this.stopCountdownTimer();
    this.countdownTimer = window.setInterval(() => {
      if (this.environment?.IsEphemeral) {
        this.nowMs = Date.now();
      }
    }, 1000);
  }

  private stopCountdownTimer() {
    if (this.countdownTimer !== undefined) {
      window.clearInterval(this.countdownTimer);
      this.countdownTimer = undefined;
    }
  }

  private isTransitionalState(stateValue: string): boolean {
    const normalized = stateValue.toLowerCase();
    return (
      normalized === 'provisioning' ||
      normalized === 'testing' ||
      normalized === 'tearingdown'
    );
  }

  private isFailedState(stateValue: string): boolean {
    const normalized = stateValue.toLowerCase();
    return (
      normalized === 'teardownfailed' ||
      normalized === 'provisionfailed' ||
      normalized === 'validationfailed'
    );
  }

  private shouldPollState(): boolean {
    if (!this.environment?.IsEphemeral || !this.environment?.EnvironmentName) {
      return false;
    }
    const state = this.environment.EphemeralState || '';
    if (state === 'TornDown' || state.toLowerCase() === 'torndown') {
      return false;
    }
    return true;
  }

  private getPollIntervalMs(): number {
    const state = this.environment?.EphemeralState || '';
    return this.isTransitionalState(state) ? 10000 : 30000;
  }

  private syncStatePolling() {
    if (this.shouldPollState()) {
      const desiredInterval = this.getPollIntervalMs();
      if (
        this.statePollTimer === undefined ||
        this.currentPollIntervalMs !== desiredInterval
      ) {
        if (this.statePollTimer !== undefined) {
          window.clearInterval(this.statePollTimer);
        }
        this.currentPollIntervalMs = desiredInterval;
        this.statePollTimer = window.setInterval(() => {
          this.pollEnvironmentState();
        }, desiredInterval);
      }
    } else {
      this.stopStatePolling();
    }
  }

  private stopStatePolling() {
    if (this.statePollTimer !== undefined) {
      window.clearInterval(this.statePollTimer);
      this.statePollTimer = undefined;
    }
    this.currentPollIntervalMs = undefined;
    if (this.statePollSubscription) {
      this.statePollSubscription.unsubscribe();
      this.statePollSubscription = undefined;
    }
  }

  private handleDeletedEnvironmentOnPoll() {
    if (!this.environment) {
      this.stopStatePolling();
      return;
    }
    this.environment = {
      ...this.environment,
      EphemeralState: 'TornDown'
    };
    this.stopStatePolling();
    this.dispatchEvent(
      new CustomEvent('ephemeral-environment-updated', {
        detail: { environment: this.environment, deleted: true },
        bubbles: true,
        composed: true
      })
    );
  }

  private pollEnvironmentState() {
    const envName = this.environment?.EnvironmentName;
    if (!this.shouldPollState() || !envName) {
      this.stopStatePolling();
      return;
    }

    this.statePollSubscription?.unsubscribe();
    const currentEnvId = this.environment?.EnvironmentId;
    const api = new RefDataEnvironmentsApi(dorcApiConfiguration);
    this.statePollSubscription = api
      .refDataEnvironmentsGet({ env: envName })
      .subscribe({
        next: (envs: EnvironmentApiModel[]) => {
          const match = (envs || []).find(
            e =>
              e.EnvironmentName?.toLowerCase() === envName.toLowerCase() &&
              (!currentEnvId || e.EnvironmentId === currentEnvId)
          );
          if (match) {
            const prevState = this.environment?.EphemeralState;
            const prevExpires = this.environment?.ExpiresAtUtc;
            const mergedMatch: EnvironmentApiModel = {
              ...match,
              UserEditable:
                match.UserEditable ?? this.environment?.UserEditable,
              IsOwner: match.IsOwner ?? this.environment?.IsOwner
            };
            this.environment = mergedMatch;
            if (
              mergedMatch.EphemeralState !== prevState ||
              mergedMatch.ExpiresAtUtc !== prevExpires
            ) {
              this.dispatchEvent(
                new CustomEvent('ephemeral-environment-updated', {
                  detail: { environment: mergedMatch },
                  bubbles: true,
                  composed: true
                })
              );
            }
            this.syncStatePolling();
          } else {
            this.handleDeletedEnvironmentOnPoll();
          }
        },
        error: (err: any) => {
          const status = err?.status ?? err?.response?.status;
          if (status === 404) {
            this.handleDeletedEnvironmentOnPoll();
          }
        }
      });
  }

  private parseUtcMs(
    dateValue: string | Date | null | undefined
  ): number | null {
    if (!dateValue) {
      return null;
    }
    if (dateValue instanceof Date) {
      const ms = dateValue.getTime();
      return Number.isNaN(ms) ? null : ms;
    }
    const trimmed = String(dateValue).trim();
    if (!trimmed) {
      return null;
    }
    const normalized =
      trimmed.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(trimmed)
        ? trimmed
        : `${trimmed}Z`;
    const parsed = Date.parse(normalized);
    return Number.isNaN(parsed) ? null : parsed;
  }

  private getRemainingInfo(): {
    label: string;
    severity: 'normal' | 'warning' | 'danger' | 'muted';
    tooltip: string;
  } {
    const env = this.environment;
    const state = (env?.EphemeralState || 'Ready').toLowerCase();
    if (state === 'torndown') {
      return {
        label: 'Torn Down',
        severity: 'muted',
        tooltip: 'Ephemeral cloud infrastructure has been destroyed'
      };
    }

    const expiresMs = this.parseUtcMs(env?.ExpiresAtUtc);
    if (expiresMs === null) {
      return {
        label: 'No Expiry Set',
        severity: this.isFailedState(state) ? 'danger' : 'normal',
        tooltip: 'TTL expiration timestamp not set'
      };
    }

    const diffMs = expiresMs - this.nowMs;
    const expiryIso = new Date(expiresMs).toUTCString();

    if (diffMs <= 0) {
      return {
        label: 'Expired',
        severity: 'danger',
        tooltip: `Expired at ${expiryIso} — eligible for automatic Dorc.Monitor reaper teardown`
      };
    }

    const totalSec = Math.floor(diffMs / 1000);
    const hours = Math.floor(totalSec / 3600);
    const minutes = Math.floor((totalSec % 3600) / 60);
    const seconds = totalSec % 60;

    const formatted =
      hours > 0
        ? `${hours}h ${minutes}m ${seconds}s`
        : `${minutes}m ${seconds}s`;

    const isWarning = diffMs < 30 * 60 * 1000;
    const isFailed = this.isFailedState(state);

    return {
      label: this.compact ? formatted : `Expires in ${formatted}`,
      severity: isFailed ? 'danger' : isWarning ? 'warning' : 'normal',
      tooltip: `Expires at ${expiryIso}`
    };
  }

  private getTierClass(tierCode: string): string {
    const normalized = tierCode.toLowerCase();
    if (normalized === 'ephemeral' || normalized === 'dv') {
      return 'tier-shared';
    }
    if (normalized === 'qa' || normalized === 'it' || normalized === 'ut') {
      return 'tier-dedicated';
    }
    if (normalized === 'pp' || normalized === 'ff') {
      return 'tier-preprod';
    }
    return '';
  }

  private getStateBadgeClass(stateValue: string): string {
    const normalized = stateValue.toLowerCase();
    switch (normalized) {
      case 'provisioning':
        return 'pill-state-provisioning';
      case 'ready':
        return 'pill-state-ready';
      case 'testing':
        return 'pill-state-testing';
      case 'tearingdown':
        return 'pill-state-tearingdown';
      case 'torndown':
        return 'pill-state-torndown';
      case 'teardownfailed':
        return 'pill-state-teardownfailed';
      case 'provisionfailed':
        return 'pill-state-provisionfailed';
      case 'validationfailed':
        return 'pill-state-validationfailed';
      default:
        return 'pill-state-ready';
    }
  }

  private getStateClass(stateValue: string): string {
    return this.getStateBadgeClass(stateValue);
  }

  private openExtendDialog = (e: Event) => {
    e.stopPropagation();
    this.customExtendMinutes = 60;
    this.extendDialogOpened = true;
  };

  private openTeardownDialog = (e: Event) => {
    e.stopPropagation();
    this.teardownReason =
      this.environment?.EphemeralState === 'TeardownFailed'
        ? 'Retry teardown from DOrc Web UI'
        : 'Manual teardown from DOrc Web UI';
    this.forceDeleteMetadata = true;
    this.teardownDialogOpened = true;
  };

  private canModifyEnvironment(): boolean {
    const canModify =
      this.environment?.UserEditable !== false ||
      this.environment?.IsOwner === true;
    return canModify;
  }

  private submitExtendTtl(minutes: number) {
    const envName = this.environment?.EnvironmentName;
    if (
      !envName ||
      minutes <= 0 ||
      this.isSubmitting ||
      !this.canModifyEnvironment()
    ) {
      return;
    }

    this.isSubmitting = true;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    api
      .ephemeralEnvironmentsEnvironmentNameExtendTtlPost({
        environmentName: envName,
        ephemeralEnvironmentExtendTtlRequestApiModel: {
          AdditionalMinutes: minutes
        }
      })
      .subscribe({
        next: (updated: EnvironmentApiModel) => {
          this.isSubmitting = false;
          this.extendDialogOpened = false;
          const mergedUpdated: EnvironmentApiModel = {
            ...updated,
            UserEditable:
              updated.UserEditable ?? this.environment?.UserEditable,
            IsOwner: updated.IsOwner ?? this.environment?.IsOwner
          };
          this.environment = mergedUpdated;
          this.nowMs = Date.now();

          Notification.show(
            `Extended TTL for '${envName}' by ${minutes} minutes.`,
            {
              theme: 'success',
              position: 'bottom-start',
              duration: 4000
            }
          );

          this.dispatchEvent(
            new CustomEvent('ephemeral-environment-updated', {
              detail: { environment: mergedUpdated },
              bubbles: true,
              composed: true
            })
          );
          this.dispatchEvent(
            new CustomEvent('environment-details-updated', {
              detail: { environment: mergedUpdated },
              bubbles: true,
              composed: true
            })
          );
        },
        error: (err: unknown) => {
          this.isSubmitting = false;
          const msg = retrieveErrorMessage(
            err,
            'Failed to extend ephemeral environment TTL.'
          );
          this.showError(msg);
        }
      });
  }

  private submitTeardown = () => {
    const envName = this.environment?.EnvironmentName;
    if (!envName || this.isSubmitting || !this.canModifyEnvironment()) {
      return;
    }

    this.isSubmitting = true;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    api
      .ephemeralEnvironmentsEnvironmentNameTeardownPost({
        environmentName: envName,
        ephemeralEnvironmentTeardownRequestApiModel: {
          Reason: this.teardownReason || 'Manual teardown from DOrc Web UI',
          ForceDeleteMetadataOnNoComponents: this.forceDeleteMetadata,
          RetainMetadataAfterTeardown: !this.forceDeleteMetadata
        }
      })
      .subscribe({
        next: (resp: EphemeralEnvironmentTeardownResponseApiModel) => {
          this.isSubmitting = false;
          this.teardownDialogOpened = false;
          this.lastDestroyRequestId = resp.DestroyRequestId ?? null;
          this.lastDestroyRequestIds = resp.DestroyRequestIds?.length
            ? [...resp.DestroyRequestIds]
            : resp.DestroyRequestId
              ? [resp.DestroyRequestId]
              : [];

          if (this.environment) {
            this.environment = {
              ...this.environment,
              EphemeralState: resp.EphemeralState || 'TearingDown'
            };
          }

          const reqList = this.lastDestroyRequestIds
            .map(id => `#${id}`)
            .join(', ');
          const isDeferredTeardown =
            !reqList &&
            !resp.EnvironmentDeleted &&
            resp.EphemeralState === 'TearingDown';
          const message = reqList
            ? `Teardown queued for '${envName}' (Request${this.lastDestroyRequestIds.length > 1 ? 's' : ''} ${reqList}).`
            : isDeferredTeardown
              ? `Teardown initiated for '${envName}' — waiting for in-flight deployment cancellation to complete.`
              : `Teardown completed for '${envName}'.`;

          Notification.show(message, {
            theme: 'primary',
            position: 'bottom-start',
            duration: 5000
          });

          this.dispatchEvent(
            new CustomEvent('ephemeral-environment-teardown', {
              detail: { response: resp, environment: this.environment },
              bubbles: true,
              composed: true
            })
          );
          this.dispatchEvent(
            new CustomEvent('ephemeral-environment-updated', {
              detail: {
                response: resp,
                environment: this.environment,
                deleted: Boolean(resp.EnvironmentDeleted)
              },
              bubbles: true,
              composed: true
            })
          );
          this.dispatchEvent(
            new CustomEvent('environment-details-updated', {
              detail: {
                response: resp,
                environment: this.environment,
                deleted: Boolean(resp.EnvironmentDeleted)
              },
              bubbles: true,
              composed: true
            })
          );
        },
        error: (err: unknown) => {
          this.isSubmitting = false;
          const msg = retrieveErrorMessage(
            err,
            'Failed to initiate ephemeral environment teardown.'
          );
          this.showError(msg);
        }
      });
  };

  private showError(msg: string) {
    const notification = new ErrorNotification();
    notification.setAttribute('errorMessage', msg);
    this.shadowRoot?.appendChild(notification);
    notification.open();
  }

  private openDestroyRequestMonitor = (e: Event, requestId?: number) => {
    e.stopPropagation();
    const targetId = requestId ?? this.lastDestroyRequestId;
    if (!targetId) {
      return;
    }
    void navigate(`/monitor-result/${targetId}`);
  };

  private renderExtendHeader = () => html`
    <span style="font-weight: 600; font-size: 1rem;">
      Extend Ephemeral TTL — ${this.environment?.EnvironmentName ?? ''}
    </span>
  `;

  private renderExtendDialog = () => html`
    <div class="dialog-content">
      <p class="dialog-desc">
        Select a preset duration or specify custom minutes to extend the expiry
        time for <strong>${this.environment?.EnvironmentName}</strong>.
      </p>
      <div class="preset-buttons">
        ${[
          { label: '+30m', minutes: 30 },
          { label: '+1h', minutes: 60 },
          { label: '+2h', minutes: 120 },
          { label: '+4h', minutes: 240 }
        ].map(
          preset => html`
            <vaadin-button
              theme="small primary contrast"
              class="preset-btn"
              ?disabled=${this.isSubmitting}
              @click=${() => this.submitExtendTtl(preset.minutes)}
            >
              ${preset.label}
            </vaadin-button>
          `
        )}
      </div>
      <vaadin-number-field
        label="Custom extension (minutes)"
        min="5"
        max="1440"
        step="15"
        .value=${String(this.customExtendMinutes)}
        @value-changed=${(e: CustomEvent<{ value: string }>) => {
          const parsed = Number.parseInt(e.detail.value, 10);
          if (!Number.isNaN(parsed) && parsed > 0) {
            this.customExtendMinutes = parsed;
          }
        }}
      ></vaadin-number-field>
    </div>
  `;

  private renderExtendFooter = () => html`
    <div class="dialog-footer">
      <vaadin-button
        theme="tertiary"
        ?disabled=${this.isSubmitting}
        @click=${() => {
          this.extendDialogOpened = false;
        }}
      >
        Cancel
      </vaadin-button>
      <vaadin-button
        theme="primary"
        ?disabled=${this.isSubmitting || this.customExtendMinutes <= 0}
        @click=${() => this.submitExtendTtl(this.customExtendMinutes)}
      >
        Extend by ${this.customExtendMinutes}m
      </vaadin-button>
    </div>
  `;

  private renderTeardownHeader = () => html`
    <span
      style="font-weight: 600; font-size: 1rem; color: var(--lumo-error-text-color);"
    >
      ${
        this.environment?.EphemeralState === 'TeardownFailed'
          ? 'Retry Ephemeral Teardown'
          : 'Tear Down Ephemeral Environment'
      }
      — ${this.environment?.EnvironmentName ?? ''}
    </span>
  `;

  private renderTeardownDialog = () => html`
    <div class="dialog-content">
      <div class="warning-callout">
        <strong>Automated Terraform Destroy:</strong> Confirming teardown queues
        a reverse-order <code>terraform destroy</code> deployment across all
        provisioned components. Because this is a non-production ephemeral
        environment, destroy plans execute automatically and remove cloud
        resources and remote <code>.tfstate</code> files.
      </div>
      <vaadin-text-field
        label="Teardown Reason"
        .value=${this.teardownReason}
        @value-changed=${(e: CustomEvent<{ value: string }>) => {
          this.teardownReason = e.detail.value;
        }}
      ></vaadin-text-field>
      <vaadin-checkbox
        label="Delete DOrc environment record after teardown completes"
        ?checked=${this.forceDeleteMetadata}
        @checked-changed=${(e: CustomEvent<{ value: boolean }>) => {
          this.forceDeleteMetadata = Boolean(e.detail.value);
        }}
      ></vaadin-checkbox>
    </div>
  `;

  private renderTeardownFooter = () => html`
    <div class="dialog-footer">
      <vaadin-button
        theme="tertiary"
        ?disabled=${this.isSubmitting}
        @click=${() => {
          this.teardownDialogOpened = false;
        }}
      >
        Cancel
      </vaadin-button>
      <vaadin-button
        theme="primary error"
        ?disabled=${this.isSubmitting}
        @click=${this.submitTeardown}
      >
        ${
          this.environment?.EphemeralState === 'TeardownFailed'
            ? 'Retry Teardown Now'
            : 'Confirm Tear Down'
        }
      </vaadin-button>
    </div>
  `;

  private renderActionButtons(state: string) {
    const normalizedState = state.toLowerCase();
    if (normalizedState === 'torndown') {
      return nothing;
    }

    const canModify =
      this.environment?.UserEditable !== false ||
      this.environment?.IsOwner === true;
    const isTerminalOrBusy =
      state === 'TearingDown' ||
      state === 'TornDown' ||
      normalizedState === 'tearingdown' ||
      normalizedState === 'torndown';
    const isTeardownFailed =
      state === 'TeardownFailed' || normalizedState === 'teardownfailed';
    const isProvisionOrValidationFailed =
      state === 'ProvisionFailed' ||
      state === 'ValidationFailed' ||
      normalizedState === 'provisionfailed' ||
      normalizedState === 'validationfailed';
    const disableExtendTtl =
      !canModify ||
      this.isSubmitting ||
      state === 'TeardownFailed' ||
      state === 'TearingDown' ||
      state === 'TornDown' ||
      isTeardownFailed ||
      isTerminalOrBusy;
    const disableTeardown =
      !canModify ||
      this.isSubmitting ||
      (isTerminalOrBusy && !isProvisionOrValidationFailed);

    const extendTooltip = !canModify
      ? 'You do not have edit permissions for this environment'
      : 'Extend TTL expiration time';
    const teardownTooltip = !canModify
      ? 'You do not have edit permissions for this environment'
      : isTeardownFailed
        ? 'Retry reverse-order terraform destroy'
        : 'Trigger reverse-order terraform destroy now';

    return html`
      <div class="actions-group">
        ${this.lastDestroyRequestIds.map(
          reqId => html`
            <vaadin-button
              theme="small tertiary"
              @click=${(e: Event) => this.openDestroyRequestMonitor(e, reqId)}
            >
              <vaadin-icon
                icon="vaadin:external-link"
                slot="prefix"
              ></vaadin-icon>
              Destroy Request #${reqId}
            </vaadin-button>
          `
        )}
        <vaadin-button
          theme="small"
          ?disabled=${disableExtendTtl}
          title=${extendTooltip}
          @click=${this.openExtendDialog}
        >
          <vaadin-icon icon="vaadin:plus-circle" slot="prefix"></vaadin-icon>
          Extend TTL
        </vaadin-button>
        <vaadin-button
          theme="small error"
          ?disabled=${disableTeardown}
          title=${teardownTooltip}
          @click=${this.openTeardownDialog}
        >
          <vaadin-icon icon="vaadin:trash" slot="prefix"></vaadin-icon>
          ${isTeardownFailed ? 'Retry Teardown' : 'Tear Down Now'}
        </vaadin-button>
      </div>
    `;
  }

  private renderDialogs() {
    return html`
      <vaadin-dialog
        .opened=${this.extendDialogOpened}
        @opened-changed=${(e: CustomEvent<{ value: boolean }>) => {
          this.extendDialogOpened = e.detail.value;
        }}
        ${dialogHeaderRenderer(this.renderExtendHeader, [this.environment])}
        ${dialogRenderer(this.renderExtendDialog, [
          this.environment,
          this.isSubmitting,
          this.customExtendMinutes
        ])}
        ${dialogFooterRenderer(this.renderExtendFooter, [
          this.isSubmitting,
          this.customExtendMinutes
        ])}
      ></vaadin-dialog>

      <vaadin-dialog
        .opened=${this.teardownDialogOpened}
        @opened-changed=${(e: CustomEvent<{ value: boolean }>) => {
          this.teardownDialogOpened = e.detail.value;
        }}
        ${dialogHeaderRenderer(this.renderTeardownHeader, [this.environment])}
        ${dialogRenderer(this.renderTeardownDialog, [
          this.teardownReason,
          this.forceDeleteMetadata
        ])}
        ${dialogFooterRenderer(this.renderTeardownFooter, [
          this.isSubmitting,
          this.environment
        ])}
      ></vaadin-dialog>
    `;
  }

  render() {
    if (!this.environment?.IsEphemeral) {
      return nothing;
    }

    const tierCode = (
      this.environment.EnvironmentTier || 'ephemeral'
    ).toLowerCase();
    const tierMeta = TIER_PROFILES_META[tierCode] || {
      code: tierCode,
      label: tierCode.toUpperCase(),
      isolationSummary: `Tier: ${tierCode}`
    };

    const rawState = this.environment.EphemeralState || 'Ready';
    const stateClass = this.getStateClass(rawState);
    const tierClass = this.getTierClass(tierCode);
    const remaining = this.getRemainingInfo();

    const countdownClass =
      remaining.severity === 'danger'
        ? 'countdown-danger'
        : remaining.severity === 'warning'
          ? 'countdown-warning'
          : '';

    const badges = html`
      <span
        class="pill pill-ephemeral"
        title="Temporary Ephemeral Environment (auto-reaped on TTL expiry)"
      >
        <vaadin-icon icon="vaadin:stopwatch"></vaadin-icon>
        EPHEMERAL
      </span>
      <span
        class="pill pill-tier ${tierClass}"
        title="${tierMeta.label} — ${tierMeta.isolationSummary}"
      >
        ${tierMeta.code}
      </span>
      <span
        class="pill ${stateClass}"
        title="Ephemeral Lifecycle State: ${rawState}"
      >
        ${rawState}
      </span>
      <span
        class="pill pill-countdown ${countdownClass}"
        title=${remaining.tooltip}
      >
        <vaadin-icon icon="vaadin:clock"></vaadin-icon>
        ${remaining.label}
      </span>
    `;

    if (this.compact) {
      return html`
        <span class="compact-strip">
          ${badges}
          ${
            this.showActions && this.showActionsInCompact
              ? this.renderActionButtons(rawState)
              : nothing
          }
        </span>
        ${
          this.showActions && this.showActionsInCompact
            ? this.renderDialogs()
            : nothing
        }
      `;
    }

    const normalizedState = rawState.toLowerCase();
    const isFailedState = this.isFailedState(rawState);

    const bannerModClass =
      remaining.severity === 'danger' || isFailedState
        ? 'banner-danger'
        : remaining.severity === 'warning' || normalizedState === 'tearingdown'
          ? 'banner-warning'
          : normalizedState === 'torndown'
            ? 'banner-muted'
            : '';

    return html`
      <div class="banner ${bannerModClass}">
        <div class="badges-group">${badges}</div>
        ${this.showActions ? this.renderActionButtons(rawState) : nothing}
      </div>
      ${this.renderDialogs()}
    `;
  }
}
