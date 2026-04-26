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
      .diff-skip {
        font-family: monaco, Consolas, 'Lucida Console', monospace;
        font-size: 11px;
        color: var(--dorc-text-secondary);
        font-style: italic;
        padding: 2px 0;
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

    // PriorJson is computed server-side via a correlated subquery — the
    // chronologically-prior audit row's Json for the same project. Diff is
    // page-boundary clean: every row except the very first audit of a project
    // (or rows for deleted projects) has a prior to compare against.
    const priorRaw = model.item.PriorJson;
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
    const display = this.collapseUnchangedRuns(ops, 3);
    const rendered = display.map(d => {
      if (d.kind === 'skip')
        return html`<div class="diff-skip">${d.line}</div>`;
      if (d.kind === 'insert')
        return html`<div class="diff-line highlight-added">${'+' + d.line}</div>`;
      if (d.kind === 'delete')
        return html`<div class="diff-line highlight-removed">${'-' + d.line}</div>`;
      return html`<div class="diff-line">${' ' + d.line}</div>`;
    });
    render(html`<div>${rendered}</div>`, root);
  };

  // Convert a full op stream into a unified-diff-style display: keep up to
  // `context` lines on either side of each change, collapse longer runs of
  // unchanged lines into a single "… N unchanged lines …" marker. Mirrors
  // the way `git diff` / `diff -U3` shrink large unchanged stretches.
  private collapseUnchangedRuns(
    ops: { type: 'keep' | 'insert' | 'delete'; line: string }[],
    context: number
  ): { kind: 'keep' | 'insert' | 'delete' | 'skip'; line: string }[] {
    const out: { kind: 'keep' | 'insert' | 'delete' | 'skip'; line: string }[] = [];
    let i = 0;
    while (i < ops.length) {
      if (ops[i].type !== 'keep') {
        out.push({ kind: ops[i].type, line: ops[i].line });
        i++;
        continue;
      }
      // Run of 'keep' starts at i.
      let runStart = i;
      while (i < ops.length && ops[i].type === 'keep') i++;
      const runEnd = i; // exclusive
      const runLen = runEnd - runStart;
      const isFirst = runStart === 0;
      const isLast = runEnd === ops.length;
      const trail = isFirst ? 0 : context; // context after the previous change
      const lead = isLast ? 0 : context; // context before the next change

      if (runLen <= trail + lead) {
        for (let j = runStart; j < runEnd; j++) {
          out.push({ kind: 'keep', line: ops[j].line });
        }
      } else {
        for (let j = runStart; j < runStart + trail; j++) {
          out.push({ kind: 'keep', line: ops[j].line });
        }
        const skipped = runLen - trail - lead;
        out.push({
          kind: 'skip',
          line: `… ${skipped} unchanged line${skipped === 1 ? '' : 's'} …`
        });
        for (let j = runEnd - lead; j < runEnd; j++) {
          out.push({ kind: 'keep', line: ops[j].line });
        }
      }
    }
    return out;
  }

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
