import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { GridItemModel } from '@vaadin/grid';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { EnvironmentContentBuildsApiModel } from '../apis/dorc-api';

@customElement('env-deployments')
export class EnvDeployments extends LitElement {
  @property({ type: Array }) builds: EnvironmentContentBuildsApiModel[] = [];

  static get styles() {
    return css`
      .success {
        background-color: #90ee90;
      }

      .failure {
        background-color: #f08080;
      }
    `;
  }

  render() {
    return html`
      <vaadin-grid
        .items="${this.builds}"
        theme="compact row-stripes no-row-borders no-border"
        all-rows-visible
        .cellClassNameGenerator="${this.cellClassNameGenerator}"
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
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          header="Update Date"
          path="UpdateDate"
          resizable
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="Status" path="State" resizable>
        </vaadin-grid-sort-column>
      </vaadin-grid>
    `;
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
