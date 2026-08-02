import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { GridCellPartNameGenerator } from '@vaadin/grid';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { EnvironmentContentBuildsApiModel } from '../apis/dorc-api';
import { NarrowListController } from '../helpers/narrow-list-controller';
import { listRowStyles, narrowListRenderers } from './dorc-list-row';
import { envDeploymentsCardNarrow } from '../row-templates/batch-row-templates';

@customElement('env-deployments')
export class EnvDeployments extends LitElement {
  /** Narrow-mode (HLPS §3.4): container-driven list rendering. */
  narrowList = new NarrowListController(this as unknown as HTMLElement & import('lit').ReactiveControllerHost);

  private _nl = narrowListRenderers(
    () => envDeploymentsCardNarrow(this).template,
    () => envDeploymentsCardNarrow(this).bar ?? {}
  );

  @property({ type: Array }) builds: EnvironmentContentBuildsApiModel[] = [];

  static get styles() {
    return [
      listRowStyles,
      css`
      vaadin-grid::part(success) {
        background-color: var(--dorc-success-bg);
        color: var(--dorc-text-primary);
      }

      vaadin-grid::part(failure) {
        background-color: var(--dorc-failure-bg);
        color: var(--dorc-text-primary);
      }
      @media (max-width: 768px) {
        vaadin-grid-cell-content {
          white-space: normal;
          word-wrap: break-word;
          overflow-wrap: break-word;
        }
      }
    `
    ];
  }

  render() {
    return html`
      <vaadin-grid
        .items="${this.builds}"
        theme="compact row-stripes no-row-borders no-border"
        all-rows-visible
        .cellPartNameGenerator="${this.cellPartNameGenerator}"
      >
        <vaadin-grid-column
          flex-grow="1"
          ?hidden="${!this.narrowList.narrow}"
          .headerRenderer="${this._nl.bar}"
          .renderer="${this._nl.row}"
        ></vaadin-grid-column>
        <vaadin-grid-sort-column ?hidden="${this.narrowList.narrow}"
          header="Component Name"
          path="ComponentName"
          resizable
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          header="Request Details"
          path="RequestDetails"
          resizable
          ?hidden="${this.narrowList.narrow}"
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          header="Update Date"
          path="UpdateDate"
          resizable
          ?hidden="${this.narrowList.narrow}"
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column ?hidden="${this.narrowList.narrow}" header="Status" path="State" resizable>
        </vaadin-grid-sort-column>
      </vaadin-grid>
    `;
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
