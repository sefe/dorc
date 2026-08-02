import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { UserApiModel } from '../apis/dorc-api';
import { NarrowListController } from '../helpers/narrow-list-controller';
import { listRowStyles, narrowListRenderers } from './dorc-list-row';
import { attachedAppUsersNarrow } from '../row-templates/batch-row-templates';

@customElement('attached-app-users')
export class AttachedUsers extends LitElement {
  /** Narrow-mode (HLPS §3.4): container-driven list rendering. */
  narrowList = new NarrowListController(this as unknown as HTMLElement & import('lit').ReactiveControllerHost);

  private _nl = narrowListRenderers(
    () => attachedAppUsersNarrow(this).template,
    () => attachedAppUsersNarrow(this).bar ?? {}
  );

  @property({ type: Array }) users: UserApiModel[] = [];

  static get styles() {
    return [
      listRowStyles,
      css`
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
        id="grid"
        .items="${this.sortedUsers}"
        theme="compact row-stripes no-row-borders no-border"
        style="height: 100%"
      >
        <vaadin-grid-column
          flex-grow="1"
          ?hidden="${!this.narrowList.narrow}"
          .headerRenderer="${this._nl.bar}"
          .renderer="${this._nl.row}"
        ></vaadin-grid-column>
        <vaadin-grid-sort-column ?hidden="${this.narrowList.narrow}" header="Name" path="DisplayName" resizable>
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column ?hidden="${this.narrowList.narrow}" header="Login ID" path="LoginId" resizable>
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="Login Type" path="LoginType" resizable
          ?hidden="${this.narrowList.narrow}">
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column ?hidden="${this.narrowList.narrow}" header="LAN ID" path="LanId" resizable>
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          header="LAN ID Type"
          path="LanIdType"
          resizable
          ?hidden="${this.narrowList.narrow}"
        >
        </vaadin-grid-sort-column>
        <vaadin-grid-sort-column header="Team" path="Team" resizable
          ?hidden="${this.narrowList.narrow}">
        </vaadin-grid-sort-column>
      </vaadin-grid>
    `;
  }

  private get sortedUsers(): UserApiModel[] {
    return [...this.users].sort((a, b) => {
    if (String(a.DisplayName).toLowerCase() > String(b.DisplayName).toLowerCase()) return 1;
    return -1;
    });
  }
}