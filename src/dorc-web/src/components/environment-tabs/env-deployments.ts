import '@polymer/paper-toggle-button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { GridItemModel } from '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { DateTimePicker } from '@vaadin/date-time-picker';
import { PageEnvBase } from './page-env-base';
import {
  EnvironmentContentBuildsApiModel,
  RefDataEnvironmentsDetailsApi
} from '../../apis/dorc-api';
import { EnvironmentContentBuildsApiModelExtended } from '../model-extensions/EnvironmentContentBuildsApiModelExtended';
import '@vaadin/date-time-picker';

@customElement('env-deployments')
export class EnvDeployments extends PageEnvBase {
  @property({ type: Boolean }) loading = true;

  @property({ type: Boolean }) applyingNewFilter = false;

  @property({ type: Array }) deployments:
    | Array<EnvironmentContentBuildsApiModelExtended>
    | undefined;

  static get styles() {
    return css`
      :host {
        display: flex;
        width: 100%;
        height: 100%;
        flex-direction: column;
      }

      .overlay {
        width: 100%;
        height: 100%;
        position: fixed;
      }

      .overlay__inner {
        width: 100%;
        height: 100%;
        position: absolute;
      }

      .overlay__content {
        left: 20%;
        position: absolute;
        top: 20%;
        transform: translate(-50%, -50%);
      }

      .spinner {
        width: 75px;
        height: 75px;
        display: inline-block;
        border-width: 2px;
        border-color: rgba(255, 255, 255, 0.05);
        border-top-color: cornflowerblue;
        animation: spin 1s infinite linear;
        border-radius: 100%;
        border-style: solid;
      }

      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }

      .small-loader {
        border: 2px solid #f3f3f3; /* Light grey */
        border-top: 2px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
      }

      .underlined-button::part(label) {
        text-decoration: underline;
      }
    `;
  }

  render() {
    return html`
      ${this.loading
        ? html` <div class="overlay">
            <div class="overlay__inner">
              <div class="overlay__content">
                <span class="spinner"></span>
              </div>
            </div>
          </div>`
        : html`
            <vaadin-details
              opened
              summary="Application Deployment Filter"
              style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px;"
            >
              <vaadin-date-time-picker
                id="deployments-filter"
                value="${Date.now()}"
                .step="${60 * 30}"
                date-placeholder="Date"
                time-placeholder="Time"
              ></vaadin-date-time-picker>
              <vaadin-button
                .disabled="${this.applyingNewFilter}"
                @click="${this.applyDateTimeFilter}"
                >Apply
              </vaadin-button>
              ${this.applyingNewFilter
                ? html` <div class="small-loader"></div>`
                : html``}
            </vaadin-details>
            <vaadin-grid
              .items="${this.deployments ?? []}"
              theme="compact row-stripes no-row-borders no-border"
              .cellClassNameGenerator="${this.cellClassNameGenerator}"
              style="height: 100%; width: 100%; flex-grow: 1"
            >
              <vaadin-grid-column
                header="Request Id"
                .renderer="${this._idRenderer.bind(this)}"
                resizable
                width="110px"
                .headerRenderer="${this.idHeaderRenderer}"
              >
              </vaadin-grid-column>
              <vaadin-grid-sort-column
                header="Component Name"
                path="ComponentName"
                resizable
                auto-width
              >
              </vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                header="Request Build Number"
                path="RequestBuildNum"
                resizable
                auto-width
              >
              </vaadin-grid-sort-column>
              <vaadin-grid-column
                header="Requested"
                .renderer="${this._dateRenderer}"
                .headerRenderer="${this.dateHeaderRenderer}"
                resizable
                auto-width
              ></vaadin-grid-column>
              <vaadin-grid-sort-column header="Status" path="State" resizable>
              </vaadin-grid-sort-column>
            </vaadin-grid>
          `}
    `;
  }

  constructor() {
    super();

    super.loadEnvironmentInfo();
  }

  applyDateTimeFilter() {
    const dateTimePicker = this.shadowRoot?.getElementById(
      'deployments-filter'
    ) as DateTimePicker;

    if (dateTimePicker.value === '') {
      alert('Both a valid Date & Time must be selected!');
      return;
    }

    this.applyingNewFilter = true;

    const dt = new Date(dateTimePicker.value);

    const api = new RefDataEnvironmentsDetailsApi();
    api
      .refDataEnvironmentsDetailsGetComponentStatuesGet({
        envName: this.environment?.EnvironmentName ?? '',
        cutoffDateTime: dt.toISOString()
      })
      .subscribe({
        next: (value: Array<EnvironmentContentBuildsApiModel>) => {
          const newDeploymentsList: Array<EnvironmentContentBuildsApiModelExtended> =
            [];
          value.forEach(ec => {
            const nec: EnvironmentContentBuildsApiModelExtended = {
              RequestId: ec.RequestId,
              State: ec.State,
              ComponentName: ec.ComponentName,
              RequestBuildNum: ec.RequestBuildNum,
              UpdateDate: ec.UpdateDate
            };
            newDeploymentsList.push(nec);
            this.getDate(nec);

            this.deployments = newDeploymentsList;
            this.applyingNewFilter = false;
          });
        },
        error: err => {
          console.log(err);
        }
      });
  }

  idHeaderRenderer(root: HTMLElement) {
    render(
      html`
        <vaadin-grid-sorter path="RequestId">Request Id</vaadin-grid-sorter>
      `,
      root
    );
  }

  dateHeaderRenderer(root: HTMLElement) {
    render(
      html`
        <vaadin-grid-sorter path="UpdatedDate">Updated Date</vaadin-grid-sorter>
      `,
      root
    );
  }

  _idRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<EnvironmentContentBuildsApiModelExtended>
  ) {
    const content = model.item as EnvironmentContentBuildsApiModelExtended;

    render(
      html`
        <vaadin-button
          class="underlined-button"
          theme="tertiary-inline"
          @click="${() => {
            const event = new CustomEvent('open-monitor-result', {
              detail: {
                request: {
                  Id: content.RequestId,
                  EnvironmentName: this.environment?.EnvironmentName,
                  BuildNumber: content.RequestBuildNum
                }
              },
              bubbles: true,
              composed: true
            });
            this.dispatchEvent(event);
          }}"
          >${content.RequestId}</vaadin-button
        >
      `,
      root
    );
  }

  _dateRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<EnvironmentContentBuildsApiModelExtended>
  ) {
    const history = model.item as EnvironmentContentBuildsApiModelExtended;
    const time = history.UpdatedDate?.toLocaleTimeString('en-GB');
    const date = history.UpdatedDate?.toLocaleDateString('en-GB', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric'
    });
    render(html` <div>${`${date} ${time}`}</div>`, root);
  }

  notifyEnvironmentContentReady() {
    this.loading = false;
    this.deployments =
      this.envContent?.Builds !== null ? this.envContent?.Builds : undefined;
    console.log('loading set to false');
  }

  cellClassNameGenerator(
    _: GridColumn,
    model: GridItemModel<EnvironmentContentBuildsApiModel>
  ) {
    const item = model.item as EnvironmentContentBuildsApiModel;
    let classes = '';
    if (item.State === 'Complete') {
      classes += ' success';
    } else if (item.State === 'Failed') {
      classes += ' failure';
    }
    return classes;
  }
}
