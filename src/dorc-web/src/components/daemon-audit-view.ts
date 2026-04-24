import { css, LitElement, PropertyValues } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { render } from 'lit';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/button';
import type { GridItemModel } from '@vaadin/grid';
import type { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { DaemonAuditApi } from '../apis/dorc-api';
import type { DaemonAuditApiModel, GetDaemonAuditListResponseDto } from '../apis/dorc-api';

/**
 * Per-daemon audit history grid.
 * Queries DaemonAuditApi.daemonAuditPut and renders a paged Vaadin grid.
 * Modelled on project-audit-data.ts for structural consistency (see FOLLOW-UPS.md F-1 for the
 * audit-UI consolidation that will unify these views in a later PR).
 */
@customElement('daemon-audit-view')
export class DaemonAuditView extends LitElement {
  @property({ type: Number }) daemonId = 0;

  @property({ type: Array }) items: DaemonAuditApiModel[] = [];

  @property({ type: Boolean }) loading = false;

  @property({ type: String }) error = '';

  static get styles() {
    return css`
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 250px);
        width: 100%;
        --divider-color: var(--dorc-border-color);
      }
      vaadin-grid#grid::part(create-type) {
        background-color: var(--dorc-success-bg);
      }
      vaadin-grid#grid::part(delete-type) {
        background-color: var(--dorc-failure-bg);
      }
      .muted {
        color: var(--dorc-text-secondary);
      }
      .error {
        color: var(--dorc-error-color);
      }
    `;
  }

  protected updated(changed: PropertyValues) {
    if (changed.has('daemonId') && this.daemonId > 0) {
      this.loadAudit();
    }
  }

  loadAudit() {
    if (this.daemonId <= 0) return;
    this.loading = true;
    this.error = '';
    const api = new DaemonAuditApi();
    api
      .daemonAuditPut({
        daemonId: this.daemonId,
        page: 1,
        limit: 200,
        pagedDataOperators: { Filters: [], SortOrders: [] }
      })
      .subscribe({
        next: (data: GetDaemonAuditListResponseDto) => {
          this.items = data.Items ?? [];
          this.loading = false;
        },
        error: (err: any) => {
          this.error = this._extractErrorMessage(err) ?? 'Failed to load audit history';
          this.loading = false;
        }
      });
  }

  render() {
    return html`
      ${this.loading
        ? html`<div class="muted">Loading audit history…</div>`
        : this.error
        ? html`<div class="error">${this.error}</div>`
        : html`
            <vaadin-grid
              id="grid"
              .items=${this.items}
              theme="compact row-stripes no-row-borders"
              .cellPartNameGenerator=${this._cellPartNameGenerator}
            >
              <vaadin-grid-sort-column
                path="Date"
                header="Date"
                direction="desc"
                .renderer=${this._dateRenderer}
                width="180px"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="Username"
                header="User"
                width="250px"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="Action"
                header="Action"
                width="120px"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="FromValue"
                header="From"
                .renderer=${this._jsonRenderer('FromValue')}
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="ToValue"
                header="To"
                .renderer=${this._jsonRenderer('ToValue')}
                resizable
              ></vaadin-grid-sort-column>
            </vaadin-grid>
          `}
    `;
  }

  private _cellPartNameGenerator = (_column: GridColumn, model: GridItemModel<DaemonAuditApiModel>) => {
    if (model.item?.Action === 'Create') return 'create-type';
    if (model.item?.Action === 'Delete') return 'delete-type';
    return '';
  };

  private _dateRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DaemonAuditApiModel>
  ) => {
    const dateStr = model.item?.Date;
    if (!dateStr) {
      render(html``, root);
      return;
    }
    const dt = new Date(dateStr);
    const formatted = `${dt.toLocaleDateString('en-GB')} ${dt.toLocaleTimeString('en-GB')}`;
    render(html`<span>${formatted}</span>`, root);
  };

  private _jsonRenderer(fieldName: 'FromValue' | 'ToValue') {
    return (root: HTMLElement, _column: GridColumn, model: GridItemModel<DaemonAuditApiModel>) => {
      const raw = model.item?.[fieldName];
      if (!raw) {
        render(html`<span class="muted">—</span>`, root);
        return;
      }
      render(html`<pre style="white-space: pre-wrap; margin: 0; font-size: 11px;">${raw}</pre>`, root);
    };
  }

  private _extractErrorMessage(err: any): string | null {
    if (err?.response) {
      if (typeof err.response === 'string') return err.response;
      if (typeof err.response.message === 'string') return err.response.message;
    }
    if (err?.message) return err.message;
    return null;
  }
}
