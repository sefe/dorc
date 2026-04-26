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
import { PagedDataSorting, RefDataProjectAuditApi } from '../apis/dorc-api';
import { PagedDataFilter, RefDataAuditApiModel } from '../apis/dorc-api/models';
import { GetRefDataAuditListResponseDto } from '../apis/dorc-api/models/GetRefDataAuditListResponseDto';
import { PageElement } from '../helpers/page-element';
import { getShortLogonName } from '../helpers/user-extensions';

@customElement('page-projects-audit')
export class PageProjectsAudit extends PageElement {
  @property({ type: Boolean }) loading = true;

  @property({ type: Boolean }) searching = false;

  // Restricts the feed to a single project when set via the ?projectId= query
  // parameter (the row-level Audit button on project-controls navigates here
  // with this set). Server-side: passed straight through as the projectId
  // parameter on PUT /RefDataProjectAudit.
  private restrictToProjectId: number | null = null;

  private userFilter = '';

  private actionFilter = '';

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        height: 100%;
        min-height: 0;
      }
      vaadin-grid#grid {
        flex: 1 1 auto;
        min-height: 0;
        overflow: auto;
        --divider-color: var(--dorc-border-color);
      }
      vaadin-grid#grid::part(create-type) {
        background-color: var(--dorc-success-bg);
      }
      vaadin-grid#grid::part(delete-type) {
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
        font-family: monaco, Consolas, 'Lucida Console', monospace;
      }
      .diff-line {
        white-space: pre-wrap;
        font-family: monaco, Consolas, 'Lucida Console', monospace;
        font-size: 11px;
      }
      .highlight-added {
        background-color: var(--dorc-success-bg);
      }
      .highlight-removed {
        background-color: var(--dorc-failure-bg);
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
        .dataProvider="${this.getProjectAudit}"
        .cellPartNameGenerator="${this.cellPartNameGenerator}"
        style="width: 100%; z-index: 1"
        ?hidden="${this.loading}"
      >
        <vaadin-grid-column
          path="Project.ProjectName"
          header="Project"
          .renderer="${this.projectNameRenderer}"
          resizable
          auto-width
          flex-grow="0"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Username"
          header="User"
          .headerRenderer="${this.userHeaderRenderer}"
          resizable
          auto-width
          flex-grow="0"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Action"
          header="Action"
          .headerRenderer="${this.actionHeaderRenderer}"
          resizable
          auto-width
          flex-grow="0"
        ></vaadin-grid-column>
        <vaadin-grid-sort-column
          path="Date"
          header="Date"
          direction="desc"
          .renderer="${this.dateRenderer}"
          resizable
          auto-width
          flex-grow="0"
        ></vaadin-grid-sort-column>
        <vaadin-grid-column
          header="Value"
          .renderer="${this.valueRenderer}"
          resizable
          flex-grow="1"
        ></vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  connectedCallback() {
    super.connectedCallback();
    const params = new URLSearchParams(window.location.search);
    const idParam = params.get('projectId');
    const parsedId = idParam ? Number(idParam) : NaN;
    if (!Number.isNaN(parsedId) && parsedId > 0) {
      this.restrictToProjectId = parsedId;
    }
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener('project-audit-loaded', this.auditLoaded as EventListener);
    this.addEventListener('searching-project-audit-started', this.searchingStarted as EventListener);
    this.addEventListener('searching-project-audit-finished', this.searchingFinished as EventListener);
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

  private cellPartNameGenerator: GridCellPartNameGenerator<RefDataAuditApiModel> = (
    _column,
    model
  ) => {
    const action = model.item?.Action;
    if (action === 'Create') return 'create-type';
    if (action === 'Delete') return 'delete-type';
    return '';
  };

  private projectNameRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<RefDataAuditApiModel>
  ) => {
    const name = model.item.Project?.ProjectName;
    if (!name) {
      render(html`<span class="muted">(deleted)</span>`, root);
      return;
    }
    render(html`<span>${name}</span>`, root);
  };

  private dateRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<RefDataAuditApiModel>
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

  private valueRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<RefDataAuditApiModel>
  ) => {
    const raw = model.item?.Json;
    if (!raw) {
      render(html`<span class="muted">—</span>`, root);
      return;
    }

    // Pretty-print the current snapshot. lit auto-escapes ${...} so even raw
    // (non-JSON) content can't inject HTML.
    const curr = this.prettyJson(raw);

    // _priorJson is stitched on by the data provider — for each row, the next
    // older audit row for the same project on this page. When present we
    // render a line-level diff; otherwise plain pre.
    const priorRaw = (model.item as RefDataAuditApiModel & { _priorJson?: string })._priorJson;
    if (!priorRaw) {
      render(html`<pre class="value">${curr}</pre>`, root);
      return;
    }
    const prev = this.prettyJson(priorRaw);

    if (prev === curr) {
      render(html`<pre class="value">${curr}</pre>`, root);
      return;
    }

    const ops = this.computeLineDiff(prev, curr);
    const lines = ops.map(op => {
      if (op.type === 'keep')
        return html`<div class="diff-line">${' ' + op.line}</div>`;
      if (op.type === 'insert')
        return html`<div class="diff-line highlight-added">${'+' + op.line}</div>`;
      return html`<div class="diff-line highlight-removed">${'-' + op.line}</div>`;
    });
    render(html`<div>${lines}</div>`, root);
  };

  private prettyJson(raw: string): string {
    try {
      return JSON.stringify(JSON.parse(raw), null, 2);
    } catch {
      return raw;
    }
  }

  private computeLineDiff(
    oldStr: string,
    newStr: string
  ): { type: 'keep' | 'insert' | 'delete'; line: string }[] {
    const oldLines = oldStr.split('\n');
    const newLines = newStr.split('\n');
    const m = oldLines.length;
    const n = newLines.length;
    const dp: number[][] = Array.from({ length: m + 1 }, () =>
      new Array(n + 1).fill(0)
    );
    for (let i = 1; i <= m; i++) {
      for (let j = 1; j <= n; j++) {
        if (oldLines[i - 1] === newLines[j - 1]) {
          dp[i][j] = dp[i - 1][j - 1] + 1;
        } else {
          dp[i][j] = Math.max(dp[i - 1][j], dp[i][j - 1]);
        }
      }
    }

    const ops: { type: 'keep' | 'insert' | 'delete'; line: string }[] = [];
    let i = m;
    let j = n;
    while (i > 0 || j > 0) {
      if (i > 0 && j > 0 && oldLines[i - 1] === newLines[j - 1]) {
        ops.push({ type: 'keep', line: oldLines[i - 1] });
        i--;
        j--;
      } else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
        ops.push({ type: 'insert', line: newLines[j - 1] });
        j--;
      } else {
        ops.push({ type: 'delete', line: oldLines[i - 1] });
        i--;
      }
    }
    ops.reverse();
    return ops;
  }

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
      new CustomEvent('searching-project-audit-started', {
        detail: {},
        bubbles: true,
        composed: true
      })
    );
    this.shadowRoot?.querySelector('vaadin-grid')?.clearCache();
  }

  getProjectAudit = (
    params: GridDataProviderParams<RefDataAuditApiModel>,
    callback: GridDataProviderCallback<RefDataAuditApiModel>
  ) => {
    const filters: PagedDataFilter[] = [];
    if (this.userFilter) {
      filters.push({ Path: 'Username', FilterValue: this.userFilter });
    }
    if (this.actionFilter) {
      filters.push({ Path: 'Action', FilterValue: this.actionFilter });
    }

    const api = new RefDataProjectAuditApi();
    api
      .refDataProjectAuditPut({
        projectId: this.restrictToProjectId ?? undefined,
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
        next: (data: GetRefDataAuditListResponseDto) => {
          const items = data.Items ?? [];
          items.forEach(
            item => (item.Username = getShortLogonName(item.Username))
          );

          // Stitch the prior project state onto each row for diff rendering.
          // Rows arrive in Date DESC order, so the "previous version" of a
          // given project is the next row in the array with the same
          // ProjectId. Cross-page boundaries are not stitched — the last
          // row(s) on a page renders as plain JSON if no same-project row
          // appears later in the page.
          for (let i = 0; i < items.length; i++) {
            const cur = items[i] as RefDataAuditApiModel & { _priorJson?: string };
            for (let j = i + 1; j < items.length; j++) {
              if (items[j].ProjectId === cur.ProjectId) {
                cur._priorJson = items[j].Json ?? undefined;
                break;
              }
            }
          }

          this.dispatchEvent(
            new CustomEvent('searching-project-audit-finished', {
              detail: {},
              bubbles: true,
              composed: true
            })
          );
          callback(items, data.TotalItems);
        },
        error: (err: any) => console.error(err),
        complete: () => {
          this.dispatchEvent(
            new CustomEvent('project-audit-loaded', {
              detail: {},
              bubbles: true,
              composed: true
            })
          );
        }
      });
  };
}
