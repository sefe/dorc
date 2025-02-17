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
import { DeploymentResultApiModel } from '../apis/dorc-api';
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
      vaadin-text-field {
        padding: 0px;
        margin: 0px;
      }
      vaadin-grid-cell-content {
        padding-top: 5px;
        padding-bottom: 5px;
        margin: 0px;
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

      <vaadin-grid
        id="grid"
        column-reordering-allowed
        multi-sort
        theme="compact row-stripes no-row-borders no-border"
        .items="${this.resultItems}"
        all-rows-visible
      >
        <vaadin-grid-column
          path="Id"
          header="Id"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="ComponentName"
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

  _logRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DeploymentResultApiModel>
  ) {
    const result = model.item as DeploymentResultApiModel;
    const first100chars = result.Log?.substring(0, 100);
    render(
      html` <div style="font-family: monospace">
        <vaadin-button
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
        ${first100chars}
      </div>`,
      root
    );
  }

  private viewLog(e: CustomEvent) {
    const result = e.detail.result as DeploymentResultApiModel;
    this.selectedLog = result.Log ?? '';
    this.dialogOpened = true;
  }

  private logDialogClosed() {
    this.dialogOpened = false;
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
            <div>${`${sDate} ${sTime}`}</div>
            <div>${`${cDate} ${cTime}`}</div>
          </vaadin-vertical-layout>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };
}
