import '@vaadin/button';
import '@vaadin/grid';
import '@vaadin/details';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, LitElement, render } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import './log-dialog';
import { DeploymentRequestAttemptApiModel, DeploymentResultAttemptApiModel } from '../apis/dorc-api';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';

@customElement('component-previous-attempts')
export class ComponentPreviousAttempts extends LitElement {
  @property({ type: Array })
  attemptItems: DeploymentRequestAttemptApiModel[] | undefined;

  @state()
  dialogOpened = false;

  @state()
  selectedLog: string | undefined;

  constructor() {
    super();

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

      .attempt-container {
        margin-bottom: 8px;
        border: 1px solid #e0e0e0;
        border-radius: 4px;
      }

      .attempt-header {
        display: flex;
        align-items: center;
        gap: 16px;
        padding: 8px 12px;
        background-color: #f5f5f5;
        font-size: var(--lumo-font-size-s);
        color: var(--lumo-secondary-text-color);
      }

      .attempt-header-item {
        display: flex;
        align-items: center;
        gap: 4px;
      }

      .attempt-header-label {
        font-weight: 500;
      }

      .component-results-grid {
        padding: 8px;
      }
    `;
  }

  render() {
    return html`
      <log-dialog
        .isOpened="${this.dialogOpened}"
        .selectedLog="${this.selectedLog}"
      >
      </log-dialog>

      ${this.attemptItems?.map(attempt => this.renderAttempt(attempt))}
    `;
  }

  private renderAttempt(attempt: DeploymentRequestAttemptApiModel) {
    const sDate = attempt.StartedTime ? new Date(attempt.StartedTime).toLocaleDateString('en-GB', { day: '2-digit', month: '2-digit', year: 'numeric' }) : '';
    const sTime = attempt.StartedTime ? new Date(attempt.StartedTime).toLocaleTimeString('en-GB') : '';
    const cDate = attempt.CompletedTime ? new Date(attempt.CompletedTime).toLocaleDateString('en-GB', { day: '2-digit', month: '2-digit', year: 'numeric' }) : '';
    const cTime = attempt.CompletedTime ? new Date(attempt.CompletedTime).toLocaleTimeString('en-GB') : '';

    return html`
      <div class="attempt-container">
        <vaadin-details summary="Attempt ${attempt.AttemptNumber} - ${attempt.Status} (${attempt.ComponentResults?.length ?? 0} components)">
          <div class="attempt-header">
            <div class="attempt-header-item">
              <span class="attempt-header-label">Started:</span>
              <span>${sDate} ${sTime}</span>
            </div>
            <div class="attempt-header-item">
              <span class="attempt-header-label">Completed:</span>
              <span>${cDate} ${cTime}</span>
            </div>
            <div class="attempt-header-item">
              <span class="attempt-header-label">User:</span>
              <span>${attempt.UserName}</span>
            </div>
            ${attempt.Log ? html`
              <vaadin-button
                theme="small"
                style="min-width: 36px; padding: 0"
                @click="${() => this.viewLog(attempt)}"
                title="View Request Log"
              >
                <vaadin-icon
                  icon="vaadin:file-text-o"
                  style="color: cornflowerblue"
                ></vaadin-icon>
              </vaadin-button>
            ` : html``}
          </div>
          <div class="component-results-grid">
            ${attempt.ComponentResults && attempt.ComponentResults.length > 0
              ? html`
                  <vaadin-grid
                    theme="compact row-stripes no-row-borders no-border"
                    .items="${attempt.ComponentResults}"
                    all-rows-visible
                  >
                    <vaadin-grid-column
                      .renderer="${this.componentNameRenderer}"
                      header="Component Name"
                      resizable
                      auto-width
                    ></vaadin-grid-column>
                    <vaadin-grid-column
                      .renderer="${this.componentTimingsRenderer}"
                      header="Timings"
                      resizable
                      auto-width
                    ></vaadin-grid-column>
                    <vaadin-grid-column
                      .renderer="${this.componentStatusRenderer}"
                      header="Status"
                      resizable
                      auto-width
                    ></vaadin-grid-column>
                    <vaadin-grid-column
                      .renderer="${this.componentLogRenderer}"
                      header="Log"
                      resizable
                      auto-width
                    ></vaadin-grid-column>
                  </vaadin-grid>
                `
              : html`<span style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">No component results archived for this attempt.</span>`
            }
          </div>
        </vaadin-details>
      </div>
    `;
  }

  componentNameRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DeploymentResultAttemptApiModel>
  ) {
    const result = model.item as DeploymentResultAttemptApiModel;
    render(
      html`<a href="scripts?search-name=${result.ComponentName}" target="_blank" style="font-size: var(--lumo-font-size-s);">${result.ComponentName}</a>`,
      root
    );
  }

  componentStatusRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DeploymentResultAttemptApiModel>
  ) {
    const result = model.item as DeploymentResultAttemptApiModel;
    render(
      html`<span style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">${result.Status}</span>`,
      root
    );
  }

  componentTimingsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DeploymentResultAttemptApiModel>
  ) => {
    const result = model.item as DeploymentResultAttemptApiModel;
    let sTime = '';
    let sDate = '';
    let cTime = '';
    let cDate = '';

    if (result.StartedTime) {
      sTime = new Date(result.StartedTime).toLocaleTimeString('en-GB');
      sDate = new Date(result.StartedTime).toLocaleDateString('en-GB', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric'
      });
    }
    if (result.CompletedTime) {
      cTime = new Date(result.CompletedTime).toLocaleTimeString('en-GB');
      cDate = new Date(result.CompletedTime).toLocaleDateString('en-GB', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric'
      });
    }

    render(
      html`
        <vaadin-vertical-layout style="line-height: var(--lumo-line-height-s);">
          <div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">
            ${`${sDate} ${sTime}`}
          </div>
          <div style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);">
            ${`${cDate} ${cTime}`}
          </div>
        </vaadin-vertical-layout>
      `,
      root
    );
  };

  componentLogRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DeploymentResultAttemptApiModel>
  ) => {
    const result = model.item as DeploymentResultAttemptApiModel;
    const first100chars = result.Log?.substring(0, 100);
    const lines = first100chars?.split(/\r?\n/);

    render(
      html`
        <table>
          <tr>
            <td>
              <vaadin-button
                theme="small"
                style="width: 36px; min-width: 36px; padding: 0"
                @click="${() => this.viewComponentLog(result)}"
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
                    html`<div style="font-size: var(--lumo-font-size-xs); color: var(--lumo-secondary-text-color);">
                      ${element}
                    </div>`
                )}
              </div>
            </td>
          </tr>
        </table>
      `,
      root
    );
  };

  private viewLog(attempt: DeploymentRequestAttemptApiModel) {
    this.selectedLog = attempt.Log ?? 'No log available';
    this.dialogOpened = true;
  }

  private viewComponentLog(result: DeploymentResultAttemptApiModel) {
    this.selectedLog = result.Log ?? 'No log available';
    this.dialogOpened = true;
  }

  private logDialogClosed() {
    this.dialogOpened = false;
  }
}