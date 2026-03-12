import { css, LitElement } from 'lit';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { GridCellPartNameGenerator } from '@vaadin/grid';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { EnvironmentContentBuildsApiModel } from '../apis/dorc-api';

@customElement('env-deployments')
export class EnvDeployments extends ResponsiveMixin(LitElement) {
  @property({ type: Array }) builds: EnvironmentContentBuildsApiModel[] = [];

  static get styles() {
    return css`
      vaadin-grid::part(success) {
        background-color: #90ee90;
      }

      vaadin-grid::part(failure) {
        background-color: #f08080;
      }
      @media (max-width: 768px) {
        vaadin-grid-cell-content {
          white-space: normal;
          word-wrap: break-word;
          overflow-wrap: break-word;
        }
      }
    `;
  }

  render() {
    return html`
      <vaadin-grid
        .items="${this.builds}"
        theme="compact row-stripes no-row-borders no-border"
        all-rows-visible
        .cellPartNameGenerator="${this.cellPartNameGenerator}"
      >
        <vaadin-grid-sort-column
          header="Component Name"
          path="ComponentName"
          resizable
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          header="Request Details"
          path="RequestDetails"
          resizable
          ?hidden="${this._narrowScreen}"
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          header="Update Date"
          path="UpdateDate"
          resizable
          ?hidden="${this._narrowScreen}"
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="Status" path="State" resizable>
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
