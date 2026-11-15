import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/dialog';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-area';
import '@vaadin/text-field';
import '@vaadin/vertical-layout';
import { css, LitElement, PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import './component-deployment-results';
import './ephemeral-environment-banner';
import './log-dialog';
import './grid-button-groups/request-controls';
import { styleMap } from 'lit/directives/style-map.js';
import '../icons/iron-icons.js';
import { Notification } from '@vaadin/notification';
import '../icons/notification-icons.js';
import '../icons/hardware-icons.js';
import { ErrorNotification } from './notifications/error-notification';
import type { DeploymentRequestApiModel } from '../apis/dorc-api';
import {
  EnvironmentApiModel,
  ProjectApiModel,
  RefDataEnvironmentsApi,
  RefDataProjectsApi
} from '../apis/dorc-api';
import './connection-status-indicator';
import { HubConnectionState } from '@microsoft/signalr';
import '@vaadin/tooltip';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

@customElement('request-status-card')
export class RequestStatusCard extends LitElement {
  @property({ type: Object })
  deployRequest!: DeploymentRequestApiModel;

  @property({ type: String })
  selectedProject = '';

  @property({ type: String }) hubConnectionState: string | undefined =
    HubConnectionState.Disconnected;

  @property({ type: Boolean }) autoRefresh = true;

  @state()
  buildNumberHref = '';

  @state()
  dialogOpened = false;

  @state()
  selectedLog: string | undefined;

  @state()
  private ephemeralEnv: EnvironmentApiModel | undefined;

  private loadedEnvName = '';

  private envLoadSubscription?: { unsubscribe(): void };

  constructor() {
    super();

    this.addEventListener(
      'request-cancelled',
      this.requestCancelled as EventListener
    );
    this.addEventListener(
      'request-restarted',
      this.requestRestarted as EventListener
    );
    this.addEventListener(
      'request-paused',
      this.requestPaused as EventListener
    );
    this.addEventListener(
      'request-resumed',
      this.requestResumed as EventListener
    );
    this.addEventListener(
      'log-dialog-closed',
      this.logDialogClosed as EventListener
    );
    this.addEventListener('refresh-requests', this.refresh as EventListener);
  }

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }

      .card-element {
        padding: 0;
        overflow: hidden;
        border-radius: 6px;
        border: 1px solid var(--dorc-border-color, rgba(128, 128, 128, 0.2));
        background: var(--dorc-bg-secondary);
        box-shadow: 0 1px 3px rgba(0, 0, 0, 0.14);
        height: fit-content;
        width: 100%;
      }

      .card-body {
        padding: 10px 14px;
      }

      .card-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 12px;
        margin-bottom: 6px;
      }

      .card-header-left {
        display: inline-flex;
        align-items: center;
        gap: 6px;
      }

      .card-element__heading {
        color: var(--dorc-text-primary);
        margin-top: 0px;
        margin-bottom: 0px;
      }

      .card-element__text {
        color: var(--dorc-text-secondary);
        margin: 2px;
      }

      .statistics-cards {
        display: flex;
        flex-wrap: wrap;
      }

      .statistics-cards__item {
        margin: 5px;
        flex-shrink: 0;
      }

      .requested-titles {
        width: 100px;
      }

      .request-error-banner {
        display: flex;
        align-items: flex-start;
        justify-content: space-between;
        gap: 12px;
        margin-top: 8px;
        padding: 8px 12px;
        border-radius: 4px;
        border: 1px solid var(--dorc-error-color);
        border-left-width: 4px;
        background: var(--lumo-error-color-10pct, rgba(231, 76, 60, 0.08));
        color: var(--dorc-text-primary);
        font-size: var(--lumo-font-size-s);
        line-height: 1.4;
      }

      .request-error-banner__message {
        flex: 1 1 auto;
        word-break: break-word;
      }

      .request-error-banner__action {
        flex-shrink: 0;
      }

      .ephemeral-request-badges {
        display: inline-flex;
        align-items: center;
        flex-wrap: wrap;
        gap: 6px;
        margin-left: 6px;
      }

      .req-indicator-pill {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 2px 8px;
        border-radius: 999px;
        font-size: 0.7rem;
        font-weight: 700;
        letter-spacing: 0.03em;
        white-space: nowrap;
      }

      .req-indicator-ephemeral {
        background: #ede9fe;
        color: #5b21b6;
        border: 1px solid #c4b5fd;
      }

      .req-indicator-auto-approve {
        background: #dcfce7;
        color: #166534;
        border: 1px solid #86efac;
      }

      .req-indicator-destroy {
        background: #fee2e2;
        color: #b91c1c;
        border: 1px solid #fca5a5;
      }
    `;
  }

  render() {
    const statusStyles = {
      color:
        this.deployRequest?.Status === 'Failed' ||
        this.deployRequest?.Status === 'Errored'
          ? 'var(--dorc-error-color)'
          : 'var(--dorc-text-secondary)'
    };
    return html`
      <log-dialog
        .isOpened="${this.dialogOpened}"
        .selectedLog="${this.selectedLog}"
      >
      </log-dialog>

      <div class="statistics-cards__item card-element">
        <connection-status-indicator
          .state="${this.hubConnectionState}"
          .autoRefresh="${this.autoRefresh}"
        ></connection-status-indicator>
        <div class="card-body">
          <div class="card-header">
            <div class="card-header-left">
              <h3 class="card-element__heading">${this.selectedProject}</h3>
              ${this.renderEphemeralRequestIndicators()}
              <vaadin-button
                aria-label="Refresh Page"
                theme="icon"
                @click="${this.refresh}"
              >
                <vaadin-tooltip
                  slot="tooltip"
                  text="Refresh Page"
                ></vaadin-tooltip>
                <vaadin-icon
                  icon="icons:refresh"
                  style="color: var(--dorc-link-color)"
                ></vaadin-icon>
              </vaadin-button>
              ${
                this.deployRequest.Log !== null &&
                this.deployRequest.Log !== '0'
                  ? html`
                      <vaadin-button
                        aria-label="View Log"
                        theme="icon"
                        @click="${this.viewLog}"
                      >
                        <vaadin-tooltip
                          slot="tooltip"
                          text="View Log"
                        ></vaadin-tooltip>
                        <vaadin-icon
                          icon="notification:sms-failed"
                          style="color: var(--dorc-error-color)"
                        ></vaadin-icon>
                      </vaadin-button>
                    `
                  : html``
              }
            </div>
            <request-controls
              .requestId="${this.deployRequest.Id ?? 0}"
              .cancelable="${
                !!this.deployRequest.UserEditable &&
                (this.deployRequest.Status === 'Running' ||
                  this.deployRequest.Status === 'Requesting' ||
                  this.deployRequest.Status === 'Pending' ||
                  this.deployRequest.Status === 'Restarting' ||
                  this.deployRequest.Status === 'Paused')
              }"
              .canRestart="${
                !!this.deployRequest.UserEditable &&
                this.deployRequest.Status !== 'Pending' &&
                this.deployRequest.Status !== 'Paused'
              }"
              .canPause="${
                !!this.deployRequest.UserEditable &&
                this.deployRequest.Status === 'Pending'
              }"
              .canResume="${
                !!this.deployRequest.UserEditable &&
                this.deployRequest.Status === 'Paused'
              }"
            ></request-controls>
          </div>
          <table>
            <tr>
              <td
                class="requested-titles card-element__text"
                style="width: 200px"
              >
                Environment:
              </td>
              <td>
                <h4
                  class="card-element__text"
                  style="display: inline-flex; align-items: center; gap: 6px; flex-wrap: wrap;"
                >
                  ${this.deployRequest?.EnvironmentName}
                  <vaadin-button
                    aria-label="Open Environment Details for ${
                      this.deployRequest?.EnvironmentName
                    }"
                    theme="icon"
                    @click="${this.openEnvironmentDetails}"
                  >
                    <vaadin-tooltip
                      slot="tooltip"
                      text="Open Environment Details for ${
                        this.deployRequest?.EnvironmentName
                      }"
                    ></vaadin-tooltip>
                    <vaadin-icon
                      icon="hardware:developer-board"
                      style="color: var(--dorc-link-color)"
                    ></vaadin-icon>
                  </vaadin-button>
                  ${
                    this.ephemeralEnv?.IsEphemeral
                      ? html`<ephemeral-environment-banner
                          .environment=${this.ephemeralEnv}
                          compact
                          .showActionsInCompact=${true}
                          @ephemeral-environment-updated=${
                            this.onEphemeralEnvironmentUpdated
                          }
                          @ephemeral-environment-teardown=${
                            this.onEphemeralEnvironmentTeardown
                          }
                        ></ephemeral-environment-banner>`
                      : html``
                  }
                </h4>
              </td>
            </tr>
            <tr>
              <td class="requested-titles card-element__text">Status:</td>
              <td>
                <h4 class="card-element__text" style=${styleMap(statusStyles)}>
                  ${this.deployRequest?.Status}
                </h4>
              </td>
            </tr>
            <tr>
              <td class="requested-titles card-element__text">Build Number:</td>
              <td>
                <h4 class="card-element__text">
                  ${
                    this.buildNumberHref === ''
                      ? html`${this.deployRequest?.BuildNumber}`
                      : html` <a href="${this.buildNumberHref}" target="_blank"
                          >${this.deployRequest?.BuildNumber}</a
                        >`
                  }
                </h4>
              </td>
            </tr>
            <tr>
              <td class="requested-titles card-element__text">
                Requested Time:
              </td>
              <td>
                <h4 class="card-element__text">
                  ${this.convertToDate(
                    this.deployRequest?.RequestedTime !== null
                      ? this.deployRequest?.RequestedTime
                      : undefined
                  )}
                </h4>
              </td>
            </tr>
            <tr>
              <td class="requested-titles card-element__text">Started Time:</td>
              <td>
                <h4 class="card-element__text">
                  ${this.convertToDate(
                    this.deployRequest?.StartedTime !== null
                      ? this.deployRequest?.StartedTime
                      : undefined
                  )}
                </h4>
              </td>
            </tr>
            <tr>
              <td class="requested-titles card-element__text">
                Completed Time:
              </td>
              <td>
                <h4 class="card-element__text">
                  ${this.convertToDate(
                    this.deployRequest?.CompletedTime !== null
                      ? this.deployRequest?.CompletedTime
                      : undefined
                  )}
                </h4>
              </td>
            </tr>
            <tr>
              <td class="requested-titles card-element__text">Submitted by:</td>
              <td>
                <h4 class="card-element__text">
                  ${this.deployRequest?.UserName}
                </h4>
              </td>
            </tr>
            ${
              this.deployRequest?.Status == 'Cancelled' ||
              this.deployRequest?.Status == 'Cancelling'
                ? html`
                    <tr>
                      <td class="requested-titles card-element__text">
                        Cancelled by:
                      </td>
                      <td>
                        <h4 class="card-element__text">
                          ${this.deployRequest?.CancelledBy}
                        </h4>
                      </td>
                    </tr>
                    <tr>
                      <td class="requested-titles card-element__text">
                        Cancelled Time:
                      </td>
                      <td>
                        <h4 class="card-element__text">
                          ${this.convertToDate(
                            this.deployRequest?.CancelledTime !== null
                              ? this.deployRequest?.CancelledTime
                              : undefined
                          )}
                        </h4>
                      </td>
                    </tr>
                  `
                : html``
            }
            ${
              this.deployRequest?.UncLogPath !== null
                ? html` <tr>
                    <td class="requested-titles card-element__text">
                      Raw Log:
                    </td>
                    <td>
                      <h4 class="card-element__text">
                        ${this.deployRequest?.UncLogPath}
                        <vaadin-button
                          aria-label="Copy Path"
                          theme="icon"
                          @click="${this.copyRawLog}"
                        >
                          <vaadin-tooltip
                            slot="tooltip"
                            text="Copy Path"
                          ></vaadin-tooltip>
                          <vaadin-icon
                            icon="icons:content-copy"
                            style="color: var(--dorc-link-color)"
                          ></vaadin-icon>
                        </vaadin-button>
                      </h4>
                    </td>
                  </tr>`
                : html``
            }
          </table>
          ${
            this.hasRequestLog()
              ? html`
                  <div class="request-error-banner" role="alert">
                    <div class="request-error-banner__message">
                      <strong>Request Error:</strong>
                      ${this.getRequestLogSummary()}
                    </div>
                    <vaadin-button
                      class="request-error-banner__action"
                      theme="small tertiary error"
                      @click="${this.viewLog}"
                    >
                      View Full Log
                    </vaadin-button>
                  </div>
                `
              : html``
          }
        </div>
      </div>
    `;
  }

  openEnvironmentDetails() {
    const api2 = new RefDataEnvironmentsApi(dorcApiConfiguration);
    api2
      .refDataEnvironmentsGet({
        env:
          this.deployRequest.EnvironmentName !== null
            ? this.deployRequest.EnvironmentName
            : undefined
      })
      .subscribe({
        next: (data: EnvironmentApiModel[]) => {
          const matched = (data || []).find(e =>
            this.matchesRequestEnvironment(e)
          );
          if (matched) {
            const event = new CustomEvent('open-env-detail', {
              detail: {
                Environment: matched
              },
              bubbles: true,
              composed: true
            });
            this.dispatchEvent(event);
          } else {
            const notification = new ErrorNotification();
            notification.setAttribute(
              'errorMessage',
              'No Environment Information located'
            );
            this.shadowRoot?.appendChild(notification);
            notification.open();
          }
        },
        error: (err: any) => {
          if (err.status === 403) {
            const notification = new ErrorNotification();
            notification.setAttribute('errorMessage', err.response);
            this.shadowRoot?.appendChild(notification);
            notification.open();
          }
          console.error(err);
        },
        complete: () => console.log('done loading environment')
      });
  }

  copyRawLog() {
    const tempInput = document.createElement('input');
    tempInput.style.setProperty('position', 'absolute');
    tempInput.style.setProperty('left', '-1000px');
    tempInput.style.setProperty('top', '-1000px');
    tempInput.value = this.deployRequest.UncLogPath ?? '';
    document.body.appendChild(tempInput);
    tempInput.select();
    document.execCommand('copy');
    document.body.removeChild(tempInput);

    Notification.show('Copied to clipboard');
  }

  requestCancelled(e: CustomEvent) {
    Notification.show(`Cancelled request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }

  requestRestarted(e: CustomEvent) {
    Notification.show(`Restarted request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
    this.refresh();
  }

  requestPaused(e: CustomEvent) {
    Notification.show(`Paused request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
    this.refresh();
  }

  requestResumed(e: CustomEvent) {
    Notification.show(`Resumed request with ID: ${e.detail.requestId}`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
    this.refresh();
  }

  convertToDate(dateVal: string | undefined): string {
    if (dateVal === undefined || dateVal === null) {
      return '';
    }
    return new Date(dateVal ?? '').toLocaleString();
  }

  disconnectedCallback(): void {
    super.disconnectedCallback();
    this.envLoadSubscription?.unsubscribe();
    this.envLoadSubscription = undefined;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.loadTargetEnvironmentIfNeeded();

    const projectsApi = new RefDataProjectsApi(dorcApiConfiguration);
    projectsApi
      .refDataProjectsProjectNameGet({
        projectName: this.deployRequest.Project ?? ''
      })
      .subscribe({
        next: (project: ProjectApiModel) => {
          const org = project.ArtefactsUrl?.split('/')[3] ?? '';
          const adProject = project.ArtefactsSubPaths?.split(';')[0] ?? '';
          const buildId = this.deployRequest?.BuildUri?.split('/').pop() ?? '';
          this.buildNumberHref = `https://dev.azure.com/${
            org + '/' + adProject
          }/_build/results?buildId=${buildId}&view=results`;
        }
      });
  }

  protected updated(_changedProperties: PropertyValues) {
    super.updated(_changedProperties);
    if (_changedProperties.has('deployRequest')) {
      this.loadTargetEnvironmentIfNeeded();
    }
  }

  private extractRequestEnvironmentId(): number | undefined {
    const details = this.deployRequest?.RequestDetails ?? '';
    const match =
      /(?:<Name>|")EphemeralEnvironmentId(?:<\/Name>|")[\s\S]{0,120}?(?:>|":\s*")(\d+)(?:<|")/i.exec(
        details
      );
    return match ? Number(match[1]) : undefined;
  }

  private matchesRequestEnvironment(env: EnvironmentApiModel): boolean {
    const envName = this.deployRequest?.EnvironmentName?.trim() ?? '';
    if (env.EnvironmentName?.toLowerCase() !== envName.toLowerCase()) {
      return false;
    }
    const reqEnvId = this.extractRequestEnvironmentId();
    return reqEnvId === undefined || env.EnvironmentId === reqEnvId;
  }

  private loadTargetEnvironmentIfNeeded() {
    const envName = this.deployRequest?.EnvironmentName?.trim() ?? '';
    if (!envName) {
      this.envLoadSubscription?.unsubscribe();
      this.envLoadSubscription = undefined;
      this.loadedEnvName = '';
      this.ephemeralEnv = undefined;
      return;
    }
    const lookupKey = `${envName}:${this.extractRequestEnvironmentId() ?? ''}`;
    if (lookupKey === this.loadedEnvName) {
      return;
    }
    this.envLoadSubscription?.unsubscribe();
    this.loadedEnvName = lookupKey;
    this.ephemeralEnv = undefined;
    const envApi = new RefDataEnvironmentsApi(dorcApiConfiguration);
    this.envLoadSubscription = envApi
      .refDataEnvironmentsGet({ env: envName })
      .subscribe({
        next: (envs: EnvironmentApiModel[]) => {
          if (this.loadedEnvName !== lookupKey) {
            return;
          }
          const match = (envs || []).find(e =>
            this.matchesRequestEnvironment(e)
          );
          this.ephemeralEnv = match;
        },
        error: () => {
          if (this.loadedEnvName !== lookupKey) {
            return;
          }
          this.ephemeralEnv = undefined;
        }
      });
  }

  private onEphemeralEnvironmentUpdated = (e: CustomEvent) => {
    const updated = e.detail?.environment as EnvironmentApiModel | undefined;
    if (updated && this.matchesRequestEnvironment(updated)) {
      this.ephemeralEnv = updated;
    }
  };

  private onEphemeralEnvironmentTeardown = (e: CustomEvent) => {
    const updated = e.detail?.environment as EnvironmentApiModel | undefined;
    if (updated && this.matchesRequestEnvironment(updated)) {
      this.ephemeralEnv = updated;
    }
    this.refresh();
  };

  private isDestroyRequest(): boolean {
    if (this.ephemeralEnv && this.ephemeralEnv.IsEphemeral !== true) {
      return false;
    }
    const details = this.deployRequest?.RequestDetails ?? '';
    return (
      /TerraformOperation[\s\S]{0,120}?Destroy/i.test(details) ||
      /IsDestroy[\s\S]{0,120}?true/i.test(details) ||
      /EphemeralTargetState[\s\S]{0,120}?TornDown/i.test(details)
    );
  }

  private hasEphemeralRequestMarkers(): boolean {
    const details = this.deployRequest?.RequestDetails ?? '';
    return (
      this.isDestroyRequest() ||
      /EphemeralTargetState/i.test(details) ||
      /DeferredValidationComponents/i.test(details)
    );
  }

  private isEphemeralRequest(): boolean {
    return (
      this.ephemeralEnv?.IsEphemeral === true ||
      (!this.ephemeralEnv && this.hasEphemeralRequestMarkers())
    );
  }

  private isAutoApproveRequest(): boolean {
    return (
      (this.ephemeralEnv?.IsEphemeral === true &&
        !this.ephemeralEnv?.EnvironmentIsProd) ||
      (!this.ephemeralEnv && this.hasEphemeralRequestMarkers())
    );
  }

  private renderEphemeralRequestIndicators() {
    const isEph = this.isEphemeralRequest();
    const isDestroy = this.isDestroyRequest();
    if (!isEph && !isDestroy) {
      return html``;
    }
    const isAutoApprove = this.isAutoApproveRequest();

    return html`
      <span class="ephemeral-request-badges">
        ${
          isEph
            ? html`
                <span
                  class="req-indicator-pill req-indicator-ephemeral"
                  title="Ephemeral Environment Deployment Request"
                >
                  <vaadin-icon
                    icon="vaadin:stopwatch"
                    style="width: 11px; height: 11px;"
                  ></vaadin-icon>
                  EPHEMERAL
                </span>
              `
            : html``
        }
        ${
          isAutoApprove
            ? html`
                <span
                  class="req-indicator-pill req-indicator-auto-approve"
                  title="Non-production ephemeral request auto-approves Terraform plans"
                >
                  AUTO-APPROVE
                </span>
              `
            : html``
        }
        ${
          isDestroy
            ? html`
                <span
                  class="req-indicator-pill req-indicator-destroy"
                  title="Automated Terraform Destroy / Teardown Request"
                >
                  <vaadin-icon
                    icon="vaadin:trash"
                    style="width: 11px; height: 11px;"
                  ></vaadin-icon>
                  DESTROY
                </span>
              `
            : html``
        }
      </span>
    `;
  }

  private refresh() {
    this.dispatchEvent(
      new CustomEvent('refresh-monitor-result', {
        detail: {
          request: this.deployRequest
        },
        bubbles: true,
        composed: true
      })
    );
  }

  private hasRequestLog(): boolean {
    const log = this.deployRequest?.Log?.trim();
    return Boolean(log && log !== '0');
  }

  private getRequestLogSummary(): string {
    const rawLog = this.deployRequest?.Log?.trim() ?? '';
    if (!rawLog) {
      return '';
    }
    const firstLine =
      rawLog
        .split(/\r?\n/)
        .map(line => line.trim())
        .find(line => line.length > 0) ?? rawLog;

    return firstLine
      .replace(/^Deployment execution failure\.\s*Exception:\s*/i, '')
      .replace(/^Deployment execution failure:\s*/i, '');
  }

  private viewLog() {
    this.selectedLog = this.deployRequest.Log ?? '';
    this.dialogOpened = true;
  }

  private logDialogClosed() {
    this.dialogOpened = false;
  }
}
