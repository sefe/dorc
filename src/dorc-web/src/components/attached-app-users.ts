import { css, LitElement } from 'lit';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { UserApiModel } from '../apis/dorc-api';

@customElement('attached-app-users')
export class AttachedUsers extends ResponsiveMixin(LitElement) {
  @property({ type: Array }) users: UserApiModel[] = [];

  static get styles() {
    return css`
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
        id="grid"
        .items="${this.users}"
        theme="compact row-stripes no-row-borders no-border"
        style="height: 100%"
      >
        <vaadin-grid-sort-column header="Name" path="DisplayName" resizable>
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="Login ID" path="LoginId" resizable>
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="Login Type" path="LoginType" resizable
          ?hidden="${this._narrowScreen}">
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="LAN ID" path="LanId" resizable>
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          header="LAN ID Type"
          path="LanIdType"
          resizable
          ?hidden="${this._narrowScreen}"
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="Team" path="Team" resizable
          ?hidden="${this._narrowScreen}">
        </vaadin-grid-sort-column>
      </vaadin-grid>
    `;
  }
}
