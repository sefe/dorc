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
import { customElement, property, state } from 'lit/decorators.js';
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

  // Audit rows whose row-details slot is currently expanded. Bound directly to
  // the grid's .detailsOpenedItems; mutations replace the array (rather than
  // pushing) so lit picks up the change and re-applies the binding.
  @state() private openedItems: RefDataAuditApiModel[] = [];

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
      /* Side-by-side diff: two columns, paired rows. Old (left) / new (right).
         Each row is a pair of cells in a CSS grid; cells get highlight classes
         based on the op kind. Long lines wrap within the cell. */
      .diff-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        column-gap: 0;
        font-family: monaco, Consolas, 'Lucida Console', monospace;
        font-size: 11px;
        line-height: 1.4;
      }
      .diff-cell {
        padding: 1px 8px;
        white-space: pre-wrap;
        word-break: break-word;
        border-right: 1px solid var(--dorc-border-color);
        min-height: 1.4em;
      }
      .diff-cell.right {
        border-right: none;
      }
      .summary-counts {
        font-size: 12px;
      }
      .summary-counts .added {
        color: var(--dorc-success-color, #4caf50);
        font-weight: 600;
      }
      .summary-counts .removed {
        color: var(--dorc-error-color);
        font-weight: 600;
      }
      .summary-sections {
        font-size: 11px;
        color: var(--dorc-text-secondary);
        margin-top: 2px;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }
      .details-pane {
        padding: 8px 12px 12px 36px;
        background: var(--dorc-bg-secondary);
      }
      /* The header sits outside the scrollable area so it always stays
         pinned at the top regardless of how vaadin-grid renders the
         row-details slot — a stickier sticky than position: sticky. */
      .diff-header {
        display: grid;
        grid-template-columns: 1fr 1fr;
        font-size: 11px;
        font-weight: 600;
        color: var(--dorc-text-secondary);
        background: var(--dorc-bg-secondary);
        padding: 4px 0;
        margin-bottom: 4px;
      }
      .diff-header > div {
        padding: 0 8px;
        border-right: 1px solid var(--dorc-border-color);
      }
      .diff-header > div.right {
        border-right: none;
      }
      .diff-pane-scroll {
        max-height: 60vh;
        overflow: auto;
      }
      vaadin-icon.chevron {
        cursor: pointer;
        color: var(--dorc-text-secondary);
        --vaadin-icon-size: 12px;
        width: 12px;
        height: 12px;
        opacity: 0.45;
        transition: opacity 120ms ease;
      }
      vaadin-icon.chevron:hover {
        opacity: 1;
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
        .detailsOpenedItems="${this.openedItems}"
        .rowDetailsRenderer="${this.detailsRenderer}"
        @active-item-changed="${this.onActiveItemChanged}"
        style="width: 100%; z-index: 1"
        ?hidden="${this.loading}"
      >
        <vaadin-grid-column
          header=""
          .renderer="${this.chevronRenderer}"
          width="40px"
          flex-grow="0"
          frozen
        ></vaadin-grid-column>
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

  // Value-cell shows a compact summary only — line counts and a few changed
  // section names. The full unified diff lives in the row-details slot
  // (detailsRenderer below) and is opened by clicking the row's chevron.
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

    const priorRaw = model.item.PriorJson;
    if (!priorRaw) {
      render(html`<span class="muted">first audit · no prior version</span>`, root);
      return;
    }

    const curr = this.prettyJson(raw);
    const prev = this.prettyJson(priorRaw);

    if (prev === curr) {
      render(html`<span class="muted">no changes</span>`, root);
      return;
    }

    const ops = this.computeLineDiff(prev, curr);
    const insertCount = ops.filter(o => o.type === 'insert').length;
    const deleteCount = ops.filter(o => o.type === 'delete').length;

    // Try to extract semantic section names from the pretty-printed diff. If
    // the JSON didn't parse, fall back to line counts only.
    const sections = this.extractChangedSections(ops);
    const sectionCount = sections.length;
    const visibleSections = sections.slice(0, 3);
    const overflow = sectionCount - visibleSections.length;

    render(
      html`
        <div class="summary-counts">
          <span class="added">+${insertCount}</span>
          <span class="removed">-${deleteCount}</span>
          lines${sectionCount > 0
            ? html` · ${sectionCount} section${sectionCount === 1 ? '' : 's'}`
            : ''}
        </div>
        ${sectionCount > 0
          ? html`<div class="summary-sections">
              ${visibleSections.join(', ')}${overflow > 0 ? ` +${overflow} more` : ''}
            </div>`
          : ''}
      `,
      root
    );
  };

  // Walk the line-LCS ops and infer a section name for each insert/delete.
  // The pretty-printed JSON has a predictable shape: top-level keys at indent
  // 2 (Project, Components), nested object keys at indent 4+, array elements
  // open `{` lines indented further. For each change op, scan backward through
  // the preceding lines (across all op types) to find the nearest enclosing
  // key/index header, building a dotted/bracketed path like
  // `Project.ArtefactsBuildRegex` or `Components[3]`.
  private extractChangedSections(
    ops: { type: 'keep' | 'insert' | 'delete'; line: string }[]
  ): string[] {
    const result: string[] = [];
    const seen = new Set<string>();
    const indentOf = (s: string): number => s.length - s.trimStart().length;
    const keyMatch = (s: string): string | null => {
      const m = s.trimStart().match(/^"([^"\\]+)"\s*:/);
      return m ? m[1] : null;
    };

    // Track a running path as we walk forward: a stack of segments at each
    // open scope. We walk linearly and maintain (segment, indent) pairs.
    type Frame = { segment: string; indent: number; arrayIndex: number | null };
    const stack: Frame[] = [];
    // Counters for array elements per stack-depth scope.
    const arrayCounters = new Map<number, number>();

    const pathString = (): string => {
      const parts: string[] = [];
      for (const f of stack) {
        if (f.arrayIndex !== null) {
          parts.push(`${f.segment}[${f.arrayIndex}]`);
        } else if (f.segment) {
          parts.push(f.segment);
        }
      }
      return parts.join('.');
    };

    for (const op of ops) {
      const line = op.line;
      const trimmed = line.trimEnd();
      const indent = indentOf(trimmed);

      // Pop frames whose indent is >= current line's indent before processing
      // a closer. (Closers `}` `]` are dedented one step from their opener.)
      const isCloser = /^\s*[}\]]/.test(trimmed);
      if (isCloser) {
        while (stack.length && stack[stack.length - 1].indent >= indent) {
          stack.pop();
        }
        continue;
      }

      // Record path for change ops at the current scope.
      if (op.type !== 'keep') {
        const k = keyMatch(line);
        // Build the path; if the changed line itself is `"Key": value,` and the
        // value is primitive, include the key in the section path.
        const base = pathString();
        let label: string;
        if (k && !/[{[]\s*$/.test(trimmed)) {
          label = base ? `${base}.${k}` : k;
        } else {
          label = base || k || '(root)';
        }
        if (label && !seen.has(label)) {
          seen.add(label);
          result.push(label);
        }
      }

      // Push a frame when this line opens an object/array.
      // Forms: `"Key": {` / `"Key": [` open a named scope.
      // `{` alone (often at indent N inside an array) opens an anonymous
      // object; counts as an array element of the enclosing array.
      const opensObject = /[{[]\s*$/.test(trimmed);
      if (opensObject) {
        const k = keyMatch(line);
        if (k) {
          stack.push({ segment: k, indent, arrayIndex: null });
          // Reset the counter scoped at this opener so its inner elements
          // start fresh.
          arrayCounters.set(indent, 0);
        } else if (/^\s*\{\s*$/.test(trimmed)) {
          // Anonymous object: array element. The enclosing scope is an array
          // (the most recent named frame whose line ended in `[`). Increment
          // its counter and reflect in the top frame's arrayIndex.
          const top = stack[stack.length - 1];
          if (top) {
            const prev = top.arrayIndex ?? -1;
            top.arrayIndex = prev + 1;
          }
        }
      }
    }

    return result;
  }

  // Row-details slot: full side-by-side diff. Old version on the left, new on
  // the right, with corresponding lines paired. No context collapsing — every
  // line is rendered (the user wanted full context for navigation). The
  // surrounding details-pane has its own internal scroll (CSS), so the grid
  // row metadata above stays visible while the user scrolls the diff content.
  private detailsRenderer = (
    root: HTMLElement,
    _grid: HTMLElement,
    model: GridItemModel<RefDataAuditApiModel>
  ) => {
    const raw = model.item?.Json;
    if (!raw) {
      render(html`<div class="details-pane muted">—</div>`, root);
      return;
    }
    const priorRaw = model.item.PriorJson;
    const curr = this.prettyJson(raw);
    if (!priorRaw) {
      render(html`<div class="details-pane"><pre class="value">${curr}</pre></div>`, root);
      return;
    }
    const prev = this.prettyJson(priorRaw);
    if (prev === curr) {
      render(html`<div class="details-pane"><pre class="value">${curr}</pre></div>`, root);
      return;
    }
    const ops = this.computeLineDiff(prev, curr);
    const rows = this.buildSideBySide(ops);
    const cells: unknown[] = [];
    for (const r of rows) {
      const leftCls = r.kind === 'delete' || r.kind === 'change'
        ? 'diff-cell highlight-removed'
        : 'diff-cell';
      const rightCls = r.kind === 'insert' || r.kind === 'change'
        ? 'diff-cell right highlight-added'
        : 'diff-cell right';
      cells.push(html`<div class="${leftCls}">${r.left ?? ''}</div>`);
      cells.push(html`<div class="${rightCls}">${r.right ?? ''}</div>`);
    }
    render(
      html`
        <div class="details-pane">
          <div class="diff-header">
            <div>Before</div>
            <div class="right">After</div>
          </div>
          <div class="diff-pane-scroll">
            <div class="diff-grid">${cells}</div>
          </div>
        </div>
      `,
      root
    );
  };

  // Convert the line-LCS ops into a side-by-side row stream. Within each run
  // of consecutive non-keep ops, deletes pair positionally with inserts:
  //   - Pair k of (deletes, inserts) → one 'change' row (left=del, right=ins)
  //   - Surplus deletes → 'delete' rows (left only)
  //   - Surplus inserts → 'insert' rows (right only)
  // Keep ops emit a single row with the same line on both sides.
  private buildSideBySide(
    ops: { type: 'keep' | 'insert' | 'delete'; line: string }[]
  ): { left: string | null; right: string | null; kind: 'keep' | 'change' | 'delete' | 'insert' }[] {
    const out: { left: string | null; right: string | null; kind: 'keep' | 'change' | 'delete' | 'insert' }[] = [];
    let i = 0;
    while (i < ops.length) {
      if (ops[i].type === 'keep') {
        out.push({ left: ops[i].line, right: ops[i].line, kind: 'keep' });
        i++;
        continue;
      }
      const dels: string[] = [];
      const ins: string[] = [];
      while (i < ops.length && ops[i].type !== 'keep') {
        if (ops[i].type === 'delete') dels.push(ops[i].line);
        else ins.push(ops[i].line);
        i++;
      }
      const max = Math.max(dels.length, ins.length);
      for (let k = 0; k < max; k++) {
        const l = dels[k];
        const r = ins[k];
        let kind: 'change' | 'delete' | 'insert';
        if (l !== undefined && r !== undefined) kind = 'change';
        else if (l !== undefined) kind = 'delete';
        else kind = 'insert';
        out.push({ left: l ?? null, right: r ?? null, kind });
      }
    }
    return out;
  }

  private chevronRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<RefDataAuditApiModel>
  ) => {
    const isOpen = this.openedItems.indexOf(model.item) !== -1;
    const icon = isOpen ? 'vaadin:chevron-down' : 'vaadin:chevron-right';
    render(html`<vaadin-icon class="chevron" icon="${icon}"></vaadin-icon>`, root);
  };

  // Vaadin grid fires active-item-changed when a row body is clicked. Toggle
  // the row's expanded state and reset activeItem so a second click on the
  // same row collapses it (otherwise vaadin treats the row as "still active"
  // and doesn't fire).
  private onActiveItemChanged = (e: CustomEvent) => {
    const item = e.detail.value as RefDataAuditApiModel | null;
    if (!item) return;
    const idx = this.openedItems.indexOf(item);
    if (idx === -1) {
      this.openedItems = [...this.openedItems, item];
    } else {
      this.openedItems = [
        ...this.openedItems.slice(0, idx),
        ...this.openedItems.slice(idx + 1)
      ];
    }
    const grid = e.currentTarget as { activeItem?: unknown; requestContentUpdate?: () => void } | null;
    if (grid) {
      grid.activeItem = null;
      // Re-render the chevron column so the icon flips.
      grid.requestContentUpdate?.();
    }
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
    // Drop any expanded rows — their item references won't survive a refetch.
    this.openedItems = [];
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
