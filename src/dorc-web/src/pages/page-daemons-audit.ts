import '@vaadin/button';
import {
  GridCellPartNameGenerator,
  GridDataProviderCallback,
  GridDataProviderParams,
  GridItemModel,
  GridSorterDefinition
} from '@vaadin/grid';
import '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid-sorter';
import '@vaadin/horizontal-layout';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import { css, PropertyValues, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { DaemonAuditApi, PagedDataSorting } from '../apis/dorc-api';
import { PagedDataFilter } from '../apis/dorc-api/models';
import { DaemonAuditApiModel } from '../apis/dorc-api/models/DaemonAuditApiModel';
import { GetDaemonAuditListResponseDto } from '../apis/dorc-api/models/GetDaemonAuditListResponseDto';
import { PageElement } from '../helpers/page-element';
import { getShortLogonName } from '../helpers/user-extensions';

@customElement('page-daemons-audit')
export class PageDaemonsAudit extends PageElement {
  @property({ type: Boolean }) loading = true;

  @property({ type: Boolean }) searching = false;

  private daemonNameFilter = '';

  private userFilter = '';

  private actionFilter = '';

  static get styles() {
    return css`
      vaadin-grid#grid {
        overflow: auto;
        height: calc(100vh - 96px);
        --divider-color: var(--dorc-border-color);
      }
      vaadin-grid#grid::part(create-type) {
        background-color: var(--dorc-success-bg);
      }
      vaadin-grid#grid::part(delete-type) {
        background-color: var(--dorc-failure-bg);
      }
      .highlight-added {
        background-color: var(--dorc-success-bg);
      }
      .highlight-removed {
        background-color: var(--dorc-failure-bg);
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
        border-color: var(--dorc-border-color);
        border-top-color: var(--dorc-link-color);
        animation: spin 1s infinite linear;
        border-radius: 100%;
        border-style: solid;
      }
      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }
      .muted {
        color: var(--dorc-text-secondary);
        font-style: italic;
      }
      pre.value {
        white-space: pre-wrap;
        margin: 0;
        font-size: 11px;
      }
    `;
  }

  render() {
    return html`
      <div
        class="overlay"
        style="z-index: 2"
        ?hidden="${!(this.loading || this.searching)}"
      >
        <div class="overlay__inner">
          <div class="overlay__content">
            <span class="spinner"></span>
          </div>
        </div>
      </div>
      <vaadin-grid
        id="grid"
        column-reordering-allowed
        multi-sort
        theme="compact row-stripes no-row-borders no-border"
        .dataProvider="${this.getDaemonAudit}"
        .cellPartNameGenerator="${this.cellPartNameGenerator}"
        style="width: 100%; z-index: 1"
        ?hidden="${this.loading}"
      >
        <vaadin-grid-column
          path="DaemonName"
          header="Daemon"
          .headerRenderer="${this.daemonNameHeaderRenderer}"
          .renderer="${this.daemonNameRenderer}"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Username"
          header="User"
          .headerRenderer="${this.userHeaderRenderer}"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Action"
          header="Action"
          .headerRenderer="${this.actionHeaderRenderer}"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-sort-column
          path="Date"
          header="Date"
          direction="desc"
          .renderer="${this.dateRenderer}"
          resizable
          auto-width
        ></vaadin-grid-sort-column>
        <vaadin-grid-column
          path="FromValue"
          header="From"
          .renderer="${this.valueRenderer('FromValue')}"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="ToValue"
          header="To"
          .renderer="${this.valueRenderer('ToValue')}"
          resizable
          auto-width
        ></vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener('daemon-audit-loaded', this.auditLoaded as EventListener);
    this.addEventListener('searching-daemon-audit-started', this.searchingStarted as EventListener);
    this.addEventListener('searching-daemon-audit-finished', this.searchingFinished as EventListener);
  }

  private searchingStarted() {
    this.searching = true;
  }

  private searchingFinished() {
    this.searching = false;
  }

  private auditLoaded() {
    this.loading = false;
  }

  private cellPartNameGenerator: GridCellPartNameGenerator<DaemonAuditApiModel> = (
    _column,
    model
  ) => {
    const action = model.item?.Action;
    if (action === 'Create') return 'create-type';
    if (action === 'Delete') return 'delete-type';
    return '';
  };

  private daemonNameRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DaemonAuditApiModel>
  ) => {
    const name = model.item.DaemonName;
    if (!name) {
      render(html`<span class="muted">(deleted)</span>`, root);
      return;
    }
    render(html`<span>${name}</span>`, root);
  };

  private dateRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DaemonAuditApiModel>
  ) => {
    const raw = model.item?.Date;
    if (!raw) {
      render(html``, root);
      return;
    }
    const dt = new Date(raw);
    const formatted = `${dt.toLocaleDateString('en-GB')} ${dt.toLocaleTimeString('en-GB')}`;
    render(html`<span>${formatted}</span>`, root);
  };

  private valueRenderer(fieldName: 'FromValue' | 'ToValue') {
    return (
      root: HTMLElement,
      _column: GridColumn,
      model: GridItemModel<DaemonAuditApiModel>
    ) => {
      const raw = model.item?.[fieldName];
      if (!raw) {
        render(html`<span class="muted">—</span>`, root);
        return;
      }

      const oldStr = model.item?.FromValue ?? '';
      const newStr = model.item?.ToValue ?? '';
      const isCreate = !oldStr && !!newStr;
      const isDelete = !!oldStr && !newStr;

      // Whole-string highlight on Create/Delete; per-character diff on Update.
      if (isCreate && fieldName === 'ToValue') {
        render(html`<pre class="value"><span class="highlight-added">${raw}</span></pre>`, root);
        return;
      }
      if (isDelete && fieldName === 'FromValue') {
        render(html`<pre class="value"><span class="highlight-removed">${raw}</span></pre>`, root);
        return;
      }
      if (oldStr === newStr) {
        render(html`<pre class="value">${raw}</pre>`, root);
        return;
      }

      const ops = this.computeDiff(oldStr, newStr);
      if (fieldName === 'FromValue') {
        const parts = ops.map(op => {
          if (op.type === 'keep') return html`${op.value}`;
          if (op.type === 'delete')
            return html`<span class="highlight-removed">${op.value}</span>`;
          return html``;
        });
        render(html`<pre class="value">${parts}</pre>`, root);
      } else {
        const parts = ops.map(op => {
          if (op.type === 'keep') return html`${op.value}`;
          if (op.type === 'insert')
            return html`<span class="highlight-added">${op.value}</span>`;
          return html``;
        });
        render(html`<pre class="value">${parts}</pre>`, root);
      }
    };
  }

  private computeDiff(
    oldStr: string,
    newStr: string
  ): { type: 'keep' | 'insert' | 'delete'; value: string }[] {
    const oldLen = oldStr.length;
    const newLen = newStr.length;
    const dp: number[][] = Array.from({ length: oldLen + 1 }, () =>
      new Array(newLen + 1).fill(0)
    );
    for (let i = 1; i <= oldLen; i++) {
      for (let j = 1; j <= newLen; j++) {
        if (oldStr[i - 1] === newStr[j - 1]) {
          dp[i][j] = dp[i - 1][j - 1] + 1;
        } else {
          dp[i][j] = Math.max(dp[i - 1][j], dp[i][j - 1]);
        }
      }
    }

    const ops: { type: 'keep' | 'insert' | 'delete'; value: string }[] = [];
    let i = oldLen;
    let j = newLen;
    while (i > 0 || j > 0) {
      if (i > 0 && j > 0 && oldStr[i - 1] === newStr[j - 1]) {
        ops.push({ type: 'keep', value: oldStr[i - 1] });
        i--;
        j--;
      } else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
        ops.push({ type: 'insert', value: newStr[j - 1] });
        j--;
      } else {
        ops.push({ type: 'delete', value: oldStr[i - 1] });
        i--;
      }
    }
    ops.reverse();

    const merged: { type: 'keep' | 'insert' | 'delete'; value: string }[] = [];
    for (const op of ops) {
      const last = merged[merged.length - 1];
      if (last && last.type === op.type) {
        last.value += op.value;
      } else {
        merged.push({ type: op.type, value: op.value });
      }
    }
    return merged;
  }

  daemonNameHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing-xs">
          <vaadin-grid-sorter path="DaemonName"></vaadin-grid-sorter>
          <vaadin-text-field
            placeholder="Daemon"
            clear-button-visible
            focus-target
            style="width: 100px"
            theme="small"
            @input="${(e: InputEvent) => {
              const tf = e.target as HTMLInputElement;
              this.daemonNameFilter = tf?.value ?? '';
              this.refreshGrid();
            }}"
          ></vaadin-text-field>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  userHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing-xs">
          <vaadin-grid-sorter path="Username">User</vaadin-grid-sorter>
          <vaadin-text-field
            placeholder="User"
            clear-button-visible
            focus-target
            style="width: 100px"
            theme="small"
            @input="${(e: InputEvent) => {
              const tf = e.target as HTMLInputElement;
              this.userFilter = tf?.value ?? '';
              this.refreshGrid();
            }}"
          ></vaadin-text-field>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  actionHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing-xs">
          <vaadin-grid-sorter path="Action">Action</vaadin-grid-sorter>
          <vaadin-text-field
            placeholder="Action"
            clear-button-visible
            focus-target
            style="width: 80px"
            theme="small"
            @input="${(e: InputEvent) => {
              const tf = e.target as HTMLInputElement;
              this.actionFilter = tf?.value ?? '';
              this.refreshGrid();
            }}"
          ></vaadin-text-field>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  private refreshGrid() {
    this.dispatchEvent(
      new CustomEvent('searching-daemon-audit-started', {
        detail: {},
        bubbles: true,
        composed: true
      })
    );
    this.shadowRoot?.querySelector('vaadin-grid')?.clearCache();
  }

  getDaemonAudit = (
    params: GridDataProviderParams<DaemonAuditApiModel>,
    callback: GridDataProviderCallback<DaemonAuditApiModel>
  ) => {
    const filters: PagedDataFilter[] = [];
    if (this.daemonNameFilter) {
      filters.push({ Path: 'DaemonName', FilterValue: this.daemonNameFilter });
    }
    if (this.userFilter) {
      filters.push({ Path: 'Username', FilterValue: this.userFilter });
    }
    if (this.actionFilter) {
      filters.push({ Path: 'Action', FilterValue: this.actionFilter });
    }

    const api = new DaemonAuditApi();
    api
      .daemonAuditPut({
        pagedDataOperators: {
          Filters: filters,
          SortOrders: params.sortOrders.map(
            (s: GridSorterDefinition): PagedDataSorting => ({
              Path: s.path,
              Direction: s.direction?.toString()
            })
          )
        },
        limit: params.pageSize,
        page: params.page + 1
      })
      .subscribe({
        next: (data: GetDaemonAuditListResponseDto) => {
          data.Items?.forEach(
            item => (item.Username = getShortLogonName(item.Username))
          );
          this.dispatchEvent(
            new CustomEvent('searching-daemon-audit-finished', {
              detail: {},
              bubbles: true,
              composed: true
            })
          );
          callback(data.Items ?? [], data.TotalItems);
        },
        error: (err: any) => console.error(err),
        complete: () => {
          this.dispatchEvent(
            new CustomEvent('daemon-audit-loaded', {
              detail: {},
              bubbles: true,
              composed: true
            })
          );
        }
      });
  };
}
