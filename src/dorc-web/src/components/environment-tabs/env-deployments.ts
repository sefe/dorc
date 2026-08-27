import { columnBodyRenderer, columnHeaderRenderer } from '@vaadin/grid/lit';
import '../dorc-spinner';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { GridCellPartNameGenerator } from '@vaadin/grid';
import { DateTimePicker } from '@vaadin/date-time-picker';
import { PageEnvBase } from './page-env-base';
import { EnvironmentContentBuildsApiModel, RefDataEnvironmentsDetailsApi } from '../../apis/dorc-api';
import { EnvironmentContentBuildsApiModelExtended } from '../model-extensions/EnvironmentContentBuildsApiModelExtended';
import '@vaadin/date-time-picker';
import '@vaadin/grid/vaadin-grid-filter';
import '@vaadin/grid/vaadin-grid-sorter';

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

      vaadin-grid::part(success) {
        background-color: var(--dorc-success-bg);
        color: var(--dorc-text-primary);
      }

      vaadin-grid::part(failure) {
        background-color: var(--dorc-failure-bg);
        color: var(--dorc-text-primary);
      }
    `;
  }

  render() {
    return html`
      ${this.loading
        ? html` <dorc-spinner></dorc-spinner>`
        : html`
            <vaadin-details
              opened
              summary="Application Deployment Filter"
              style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; margin: 0px;"
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
              .cellPartNameGenerator="${this.cellPartNameGenerator}"
              style="height: 100%; width: 100%; flex-grow: 1"
            >
              <vaadin-grid-column
                header="Request Id"
                ${columnBodyRenderer(this._idRenderer, [])}
                resizable
                width="110px"
                ${columnHeaderRenderer(this.idHeaderRenderer, [])}
              >
              </vaadin-grid-column>
              <vaadin-grid-column
                path="ComponentName"
                resizable
                auto-width
                ${columnHeaderRenderer(this.componentNameHeaderRenderer, [])}
              >
              </vaadin-grid-column>
              <vaadin-grid-column
                path="RequestBuildNum"
                resizable
                auto-width
                ${columnHeaderRenderer(this.requestNumberHeaderRenderer, [])}
              >
              </vaadin-grid-column>
              <vaadin-grid-column
                header="Requested"
                ${columnBodyRenderer(this._dateRenderer, [])}
                ${columnHeaderRenderer(this.dateHeaderRenderer, [])}
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
          this.deployments = value.map(ec => {
            const nec: EnvironmentContentBuildsApiModelExtended = {
              RequestId: ec.RequestId,
              State: ec.State,
              ComponentName: ec.ComponentName,
              RequestBuildNum: ec.RequestBuildNum,
              UpdateDate: ec.UpdateDate
            };
            this.getDate(nec);
            return nec;
          });
        },
        error: err => {
          this.applyingNewFilter = false;
          console.log(err);
        },
        complete: () => {
          this.applyingNewFilter = false;
        }
      });
  }

  idHeaderRenderer() {
    return html`
        <vaadin-grid-sorter path="RequestId">Request Id</vaadin-grid-sorter>
      `;
  }

  dateHeaderRenderer() {
    return html`
        <vaadin-grid-sorter path="UpdatedDate">Updated Date</vaadin-grid-sorter>
      `;
  }

  componentNameHeaderRenderer() {
    return html`<vaadin-grid-sorter path="ComponentName">Component Name</vaadin-grid-sorter>
        <vaadin-grid-filter path="ComponentName">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100%"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>`;
  }

  requestNumberHeaderRenderer() {
    return html`<vaadin-grid-sorter path="RequestBuildNum">Request Build Number</vaadin-grid-sorter>
        <vaadin-grid-filter path="RequestBuildNum">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100%"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>`;
  }

  _idRenderer(
    item: EnvironmentContentBuildsApiModelExtended
  ) {
    const content = item as EnvironmentContentBuildsApiModelExtended;

    return html`
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
      `;
  }

  _dateRenderer(
    item: EnvironmentContentBuildsApiModelExtended
  ) {
    const history = item as EnvironmentContentBuildsApiModelExtended;
    const time = history.UpdatedDate?.toLocaleTimeString('en-GB');
    const date = history.UpdatedDate?.toLocaleDateString('en-GB', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric'
    });
    return html` <div>${`${date} ${time}`}</div>`;
  }

  notifyEnvironmentContentReady() {
    this.loading = false;
    this.deployments =
      this.envContent?.Builds !== null ? this.envContent?.Builds : undefined;
    console.log('loading set to false');
  }

  cellPartNameGenerator: GridCellPartNameGenerator<EnvironmentContentBuildsApiModel> = (
    _column,
    model
  ) => {
    const item = model.item;
    let parts = '';
    if (item.State === 'Complete') {
      parts += ' success';
    } else if (item.State === 'Failed') {
      parts += ' failure';
    }
    return parts;
  };
}
