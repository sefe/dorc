import { LitElement, css, html, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import {
  SideBySideRow,
  buildSideBySide,
  computeCharDiff,
  computeLineDiff
} from '../helpers/text-diff';

type DiffBlock =
  | { kind: 'rows'; rows: SideBySideRow[] }
  | { kind: 'fold'; rows: SideBySideRow[]; index: number };

/**
 * Side-by-side JSON/text diff. Unchanged runs are folded behind an expandable
 * "N unchanged lines" row when at least four lines would be hidden beyond the
 * `context` lines kept around each change; set `context` to a negative value
 * to show every line.
 */
@customElement('hegs-json-diff')
export class HegsJsonDiff extends LitElement {
  static styles = css`
    :host {
      display: flex;
      flex-direction: column;
      min-height: 0;
      min-width: 0;
      --audit-row-add-bg: color-mix(
        in srgb,
        var(--dorc-success-bg) 35%,
        var(--dorc-bg-primary)
      );
      --audit-row-remove-bg: color-mix(
        in srgb,
        var(--dorc-failure-bg) 35%,
        var(--dorc-bg-primary)
      );
      --audit-char-add-bg: var(--dorc-success-bg);
      --audit-char-remove-bg: var(--dorc-failure-bg);
    }
    .diff-header {
      display: grid;
      grid-template-columns: 1fr 1fr;
      font-size: var(--lumo-font-size-xs);
      font-weight: 600;
      color: var(--dorc-text-secondary);
      background: var(--dorc-bg-secondary);
      border: 1px solid var(--dorc-border-color);
      border-bottom: none;
      border-radius: var(--lumo-border-radius-s) var(--lumo-border-radius-s) 0 0;
      padding: 4px 0;
    }
    .diff-header > div {
      padding: 0 8px;
      border-right: 1px solid var(--dorc-border-color);
    }
    .diff-header > div.right {
      border-right: none;
    }
    .diff-scroll {
      flex: 1;
      min-height: 0;
      overflow: auto;
      border: 1px solid var(--dorc-border-color);
      border-radius: 0 0 var(--lumo-border-radius-s) var(--lumo-border-radius-s);
      background: var(--dorc-bg-primary);
    }
    .diff-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      font-family: var(
        --dorc-mono-font,
        monaco,
        Consolas,
        'Lucida Console',
        monospace
      );
      font-size: var(--lumo-font-size-xs);
      line-height: 1.5;
    }
    .diff-cell {
      padding: 1px 8px;
      white-space: pre-wrap;
      word-break: break-word;
      border-right: 1px solid var(--dorc-border-color);
      min-height: 1.5em;
    }
    .diff-cell.right {
      border-right: none;
    }
    .line-add {
      background-color: var(--audit-row-add-bg);
    }
    .line-remove {
      background-color: var(--audit-row-remove-bg);
    }
    .highlight {
      background-color: var(--audit-char-add-bg);
    }
    .highlight-removed {
      background-color: var(--audit-char-remove-bg);
    }
    .fold {
      grid-column: 1 / -1;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--lumo-space-xs);
      padding: 2px 8px;
      font-family: inherit;
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary);
      background: var(--dorc-bg-secondary);
      border: none;
      border-top: 1px dashed var(--dorc-border-color);
      border-bottom: 1px dashed var(--dorc-border-color);
      cursor: pointer;
      width: 100%;
    }
    .fold:hover {
      color: var(--dorc-text-primary, inherit);
    }
    .summary {
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary);
      padding: 0 0 var(--lumo-space-xs);
    }
    .summary .added {
      color: var(--dorc-success-color, #4caf50);
      font-weight: 600;
    }
    .summary .removed {
      color: var(--dorc-error-color);
      font-weight: 600;
    }
    .empty {
      font-size: var(--lumo-font-size-s);
      color: var(--dorc-text-secondary);
      font-style: italic;
      padding: var(--lumo-space-s);
    }
  `;

  @property({ type: String, attribute: 'old-text' }) oldText = '';

  @property({ type: String, attribute: 'new-text' }) newText = '';

  @property({ type: String, attribute: 'before-label' }) beforeLabel = 'Before';

  @property({ type: String, attribute: 'after-label' }) afterLabel = 'After';

  @property({ type: Number }) context = 3;

  /** Lines differing only in whitespace are treated as unchanged (default on). */
  @property({ type: Boolean, attribute: 'ignore-whitespace' })
  ignoreWhitespace = true;

  @state() private expandedFolds = new Set<number>();

  protected willUpdate(changed: Map<string, unknown>) {
    if (changed.has('oldText') || changed.has('newText')) {
      this.expandedFolds = new Set();
    }
  }

  render() {
    const ops = computeLineDiff(this.oldText, this.newText, {
      ignoreWhitespace: this.ignoreWhitespace
    });
    if (ops.every(o => o.type === 'keep')) {
      return html`<p class="empty">
        No
        differences${
          this.oldText !== this.newText ? ' other than whitespace' : ''
        }.
      </p>`;
    }
    const added = ops.filter(o => o.type === 'insert').length;
    const removed = ops.filter(o => o.type === 'delete').length;
    const blocks = this.fold(buildSideBySide(ops));
    return html`
      <div class="summary">
        <span class="added">+${added}</span>
        <span class="removed">-${removed}</span>
        lines
      </div>
      <div class="diff-header">
        <div>${this.beforeLabel}</div>
        <div class="right">${this.afterLabel}</div>
      </div>
      <div class="diff-scroll">
        <div class="diff-grid">
          ${blocks.map(block =>
            block.kind === 'fold'
              ? html`<button
                  type="button"
                  class="fold"
                  @click=${() => this.expand(block.index)}
                >
                  ⋯ ${block.rows.length} unchanged lines
                </button>`
              : block.rows.map(row => this.renderRow(row))
          )}
        </div>
      </div>
    `;
  }

  private expand(index: number) {
    const next = new Set(this.expandedFolds);
    next.add(index);
    this.expandedFolds = next;
  }

  private fold(rows: SideBySideRow[]): DiffBlock[] {
    if (this.context < 0) {
      return [{ kind: 'rows', rows }];
    }
    const blocks: DiffBlock[] = [];
    let foldIndex = 0;
    let i = 0;
    while (i < rows.length) {
      if (rows[i].kind !== 'keep') {
        blocks.push({ kind: 'rows', rows: [rows[i]] });
        i++;
        continue;
      }
      let j = i;
      while (j < rows.length && rows[j].kind === 'keep') j++;
      const run = rows.slice(i, j);
      const leading = i === 0 ? 0 : this.context;
      const trailing = j === rows.length ? 0 : this.context;
      const hiddenCount = run.length - leading - trailing;
      if (hiddenCount >= 4) {
        const index = foldIndex++;
        if (leading) blocks.push({ kind: 'rows', rows: run.slice(0, leading) });
        const hidden = run.slice(leading, run.length - trailing);
        blocks.push(
          this.expandedFolds.has(index)
            ? { kind: 'rows', rows: hidden }
            : { kind: 'fold', rows: hidden, index }
        );
        if (trailing)
          blocks.push({ kind: 'rows', rows: run.slice(run.length - trailing) });
      } else {
        blocks.push({ kind: 'rows', rows: run });
      }
      i = j;
    }
    return blocks;
  }

  private renderRow(row: SideBySideRow) {
    if (row.kind === 'change' && row.left !== null && row.right !== null) {
      const charOps = computeCharDiff(row.left, row.right);
      return html`<div class="diff-cell line-remove">
          ${charOps.map(op =>
            op.type === 'keep'
              ? op.value
              : op.type === 'delete'
                ? html`<span class="highlight-removed">${op.value}</span>`
                : nothing
          )}
        </div>
        <div class="diff-cell right line-add">
          ${charOps.map(op =>
            op.type === 'keep'
              ? op.value
              : op.type === 'insert'
                ? html`<span class="highlight">${op.value}</span>`
                : nothing
          )}
        </div>`;
    }
    const leftCls =
      row.kind === 'delete' ? 'diff-cell line-remove' : 'diff-cell';
    const rightCls =
      row.kind === 'insert' ? 'diff-cell right line-add' : 'diff-cell right';
    return html`<div class=${leftCls}>${row.left ?? ''}</div>
      <div class=${rightCls}>${row.right ?? ''}</div>`;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'hegs-json-diff': HegsJsonDiff;
  }
}
