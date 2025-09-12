import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { UserApiModel } from '../apis/dorc-api';

@customElement('attached-app-users')
export class AttachedUsers extends LitElement {
  @property({ type: Array }) users: UserApiModel[] = [];

  static get styles() {
    return css``;
  }

  render() {
    return html`
      <vaadin-grid
        id="grid"
        .items="${this.users}"
        theme="compact row-stripes no-row-borders no-border"
        style="height: 100%"
      >
        <vaadin-grid-sort-column header="Name" path="DisplayName" resizable>
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="Login ID" path="LoginId" resizable>
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="Login Type" path="LoginType" resizable>
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="LAN ID" path="LanId" resizable>
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          header="LAN ID Type"
          path="LanIdType"
          resizable
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="Team" path="Team" resizable>
        </vaadin-grid-sort-column>
      </vaadin-grid>
    `;
  }
}
