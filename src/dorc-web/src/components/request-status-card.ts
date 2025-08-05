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
import './log-dialog';
import './grid-button-groups/request-controls';
import { styleMap } from 'lit/directives/style-map.js';
import '../icons/iron-icons.js';
import { Notification } from '@vaadin/notification';
import '../icons/notification-icons.js';
import '../icons/hardware-icons.js';
import { ErrorNotification } from './notifications/error-notification';
import { BuildsApi } from '../apis/azure-devops-build';
import type { DeploymentRequestApiModel } from '../apis/dorc-api';
import {
  EnvironmentApiModel,
  ProjectApiModel,
  RefDataEnvironmentsApi,
  RefDataProjectsApi
} from '../apis/dorc-api';

@customElement('request-status-card')
export class RequestStatusCard extends LitElement {
  @property({ type: Object })
  deployRequest!: DeploymentRequestApiModel;

  @property({ type: String })
  selectedProject = '';

  @state()
  buildNumberHref = '';

  @state()
  dialogOpened = false;

  @state()
  selectedLog: string | undefined;

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
      'log-dialog-closed',
      this.logDialogClosed as EventListener
    );
  }

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }

      .card-element {
        padding: 10px;
        box-shadow: 1px 2px 3px rgba(0, 0, 0, 0.2);
        height: fit-content;
        width: 100%;
      }

      .card-element__heading {
        color: black;
        margin-top: 0px;
        margin-bottom: 0px;
      }

      .card-element__text {
        color: gray;
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
    `;
  }

  render() {
    const statusStyles = {
      color:
        this.deployRequest?.Status === 'Failed' ||
        this.deployRequest?.Status === 'Errored'
          ? 'red'
          : 'gray'
    };
    return html`
      <log-dialog
        .isOpened="${this.dialogOpened}"
        .selectedLog="${this.selectedLog}"
      >
      </log-dialog>

      <div class="statistics-cards__item card-element">
        <table>
          <tr>
            <td style="vertical-align: middle">
              <h3 class="card-element__heading">${this.selectedProject}</h3>
            </td>
            <td style="vertical-align: middle">
              <vaadin-button
                title="Refresh Page"
                theme="icon"
                @click="${this.refresh}"
              >
                <vaadin-icon
                  icon="icons:refresh"
                  style="color: cornflowerblue"
                ></vaadin-icon>
              </vaadin-button>
            </td>
            ${this.deployRequest.Log !== null && this.deployRequest.Log !== '0'
              ? html` <td style="vertical-align: middle">
                  <vaadin-button
                    title="View Log"
                    theme="icon"
                    @click="${this.viewLog}"
                  >
                    <vaadin-icon
                      icon="notification:sms-failed"
                      style="color: indianred"
                    ></vaadin-icon>
                  </vaadin-button>
                </td>`
              : html``}
          </tr>
        </table>
        <table>
          <tr>
            <td
              class="requested-titles card-element__text"
              style="width: 200px"
            >
              Environment:
            </td>
            <td>
              <h4 class="card-element__text">
                ${this.deployRequest?.EnvironmentName}
                <vaadin-button
                  title="Open Environment Details for ${this.deployRequest
                    ?.EnvironmentName}"
                  theme="icon"
                  @click="${this.openEnvironmentDetails}"
                >
                  <vaadin-icon
                    icon="hardware:developer-board"
                    style="color: cornflowerblue"
                  ></vaadin-icon>
                </vaadin-button>
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
                ${this.buildNumberHref === ''
                  ? html`${this.deployRequest?.BuildNumber}`
                  : html` <a href="${this.buildNumberHref}" target="_blank"
                      >${this.deployRequest?.BuildNumber}</a
                    >`}
              </h4>
            </td>
          </tr>
          <tr>
            <td class="requested-titles card-element__text">Requested Time:</td>
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
            <td class="requested-titles card-element__text">Completed Time:</td>
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
          ${this.deployRequest?.UncLogPath !== null
            ? html` <tr>
                <td class="requested-titles card-element__text">Raw Log:</td>
                <td>
                  <h4 class="card-element__text">
                    ${this.deployRequest?.UncLogPath}
                    <vaadin-button
                      title="Copy Path"
                      theme="icon"
                      @click="${this.copyRawLog}"
                    >
                      <vaadin-icon
                        icon="icons:content-copy"
                        style="color: cornflowerblue"
                      ></vaadin-icon>
                    </vaadin-button>
                  </h4>
                </td>
              </tr>`
            : html``}
        </table>
        <request-controls
          style="position: absolute; right: 15px; top: 65px;"
          .requestId="${this.deployRequest.Id}"
          .cancelable="${this.deployRequest.UserEditable &&
          (this.deployRequest.Status === 'Running' ||
            this.deployRequest.Status === 'Requesting' ||
            this.deployRequest.Status === 'Pending' ||
            this.deployRequest.Status === 'Restarting')}"
          .canRestart="${this.deployRequest.UserEditable &&
          this.deployRequest.Status !== 'Pending'}"
        ></request-controls>
      </div>
    `;
  }

  openEnvironmentDetails() {
    const api2 = new RefDataEnvironmentsApi();
    api2
      .refDataEnvironmentsGet({
        env:
          this.deployRequest.EnvironmentName !== null
            ? this.deployRequest.EnvironmentName
            : undefined
      })
      .subscribe({
        next: (data: EnvironmentApiModel[]) => {
          if (data[0] !== null) {
            const event = new CustomEvent('open-env-detail', {
              detail: {
                Environment: data[0]
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

  convertToDate(dateVal: string | undefined): string {
    if (dateVal === undefined || dateVal === null) {
      return '';
    }
    return new Date(dateVal ?? '').toLocaleString();
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    const projectsApi = new RefDataProjectsApi();
    projectsApi
      .refDataProjectsProjectNameGet({ projectName: this.deployRequest.Project ?? '' })
      .subscribe({
        next: (project: ProjectApiModel) => {
          const org = project.ArtefactsUrl?.split('/')[3] ?? '';
          const adProject = project.ArtefactsSubPaths?.split(';')[0] ?? '';
          const buildId = this.deployRequest?.BuildUri?.split('/').pop() ?? '';
          this.buildNumberHref = `https://dev.azure.com/${
            org + '/' + adProject
          }/_build/results?buildId=${
            buildId
          }&view=results`;
        }
      });
  }

  private refresh() {
    this.deployRequest.Status = 'Refreshing...';
    this.requestUpdate();
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

  private viewLog() {
    this.selectedLog = this.deployRequest.Log ?? '';
    this.dialogOpened = true;
  }

  private logDialogClosed() {
    this.dialogOpened = false;
  }
}
