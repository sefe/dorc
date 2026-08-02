import { css, html, nothing, render as litRender, TemplateResult } from 'lit';

/**
 * The row-template contract (HLPS §3.4): each migrated view declares, in one
 * reviewable module, what fills each slot of its narrow list row. Slots per
 * the approved slot table — primary is required; the rest are per-view.
 *
 * The disclosure affordance carries aria-expanded itself: Vaadin's
 * row-details implementation exposes no expanded state (aria-expanded is
 * tree-grid-only — HLPS R3-F1), so the template owns the announcement.
 */
export interface ListRowTemplate<T> {
  /** The record's identity — typographically dominant, full row width. */
  primary: (item: T) => TemplateResult;
  /** Muted secondary fields, inline. */
  metadata?: (item: T) => TemplateResult;
  /** Status chip driven by the item model (not cellPartNameGenerator). */
  chip?: (item: T) => { label: string; kind: 'success' | 'failure' | 'neutral' } | null;
  /** Per-row actions, rendered in the trailing actions area. */
  actions?: (item: T) => TemplateResult;
  /** Row-details content: the fields that are hidden columns today. */
  details?: (item: T) => TemplateResult;
}

/**
 * Styles for hosts composing list rows. Cell content is slotted into the
 * host's tree, so these apply from the host component's static styles.
 * Text wrapping is opt-in here (Vaadin default is nowrap+ellipsis).
 */
export const listRowStyles = css`
  /* ResizeObserver reports 0 width for inline hosts, which the controller
     ignores — a migrated component must lay out as a block. Components with
     their own :host display rule override this (their css follows in the
     styles array). */
  :host {
    display: block;
  }
  .dorc-list-row {
    display: flex;
    align-items: flex-start;
    gap: var(--lumo-space-s);
    padding: var(--lumo-space-xs) 0;
    white-space: normal;
    overflow-wrap: anywhere;
    width: 100%;
  }
  .dorc-list-row__body {
    flex: 1;
    min-width: 0;
  }
  .dorc-list-row__primary {
    font-weight: 600;
    color: var(--lumo-body-text-color);
    line-height: var(--lumo-line-height-s);
  }
  .dorc-list-row__metadata {
    color: var(--lumo-secondary-text-color);
    font-size: var(--lumo-font-size-s);
    line-height: var(--lumo-line-height-s);
    margin-top: 2px;
  }
  .dorc-list-row__chip {
    display: inline-block;
    padding: 0 var(--lumo-space-s);
    border-radius: var(--lumo-border-radius-l);
    font-size: var(--lumo-font-size-xs);
    font-weight: 600;
    line-height: 1.6;
    vertical-align: middle;
    margin-left: var(--lumo-space-s);
    background: var(--lumo-contrast-10pct);
    color: var(--lumo-secondary-text-color);
  }
  .dorc-list-row__chip--success {
    background: var(--lumo-success-color-10pct);
    color: var(--lumo-success-text-color);
  }
  .dorc-list-row__chip--failure {
    background: var(--lumo-error-color-10pct);
    color: var(--lumo-error-text-color);
  }
  .dorc-list-row__actions {
    flex: none;
    display: flex;
    align-items: center;
    gap: var(--lumo-space-xs);
    /* WCAG 2.5.8: min 24px targets; lumo buttons exceed this */
  }
  .dorc-list-bar {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--lumo-space-s);
    padding: var(--lumo-space-xs) 0;
    width: 100%;
    white-space: normal;
  }
  .dorc-list-bar > * {
    min-width: 0;
  }
  .dorc-list-details {
    display: grid;
    grid-template-columns: max-content 1fr;
    gap: 2px var(--lumo-space-m);
    padding: var(--lumo-space-xs) var(--lumo-space-s);
    font-size: var(--lumo-font-size-s);
    white-space: normal;
    overflow-wrap: anywhere;
  }
  .dorc-list-details dt {
    color: var(--lumo-secondary-text-color);
    margin: 0;
  }
  .dorc-list-details dd {
    margin: 0;
  }
`;

/** Render one list row from a template. */
export function renderListRow<T>(
  template: ListRowTemplate<T>,
  item: T
): TemplateResult {
  const chip = template.chip?.(item) ?? null;
  return html`
    <div class="dorc-list-row">
      <div class="dorc-list-row__body">
        <div class="dorc-list-row__primary">
          ${template.primary(item)}${chip
            ? html`<span
                class="dorc-list-row__chip dorc-list-row__chip--${chip.kind}"
                >${chip.label}</span
              >`
            : nothing}
        </div>
        ${template.metadata
          ? html`<div class="dorc-list-row__metadata">
              ${template.metadata(item)}
            </div>`
          : nothing}
      </div>
      ${template.actions
        ? html`<div class="dorc-list-row__actions">${template.actions(item)}</div>`
        : nothing}
    </div>
  `;
}

/** Render a details panel as a label/value grid (dl semantics for SC5). */
export function renderListDetails(
  fields: Array<{ label: string; value: TemplateResult | string }>
): TemplateResult {
  return html`
    <dl class="dorc-list-details">
      ${fields.map(
        f => html`<dt>${f.label}</dt>
          <dd>${f.value}</dd>`
      )}
    </dl>
  `;
}

/**
 * Render the narrow-mode bar (hosted in the list column's headerRenderer —
 * DECISION-bar-mechanism.md). Sections: view-level controls, filters, sort.
 */
export function renderListBar(sections: {
  controls?: TemplateResult;
  filters?: TemplateResult;
  sort?: TemplateResult;
}): TemplateResult {
  return html`
    <div class="dorc-list-bar" role="toolbar" aria-label="List controls">
      ${sections.controls ?? nothing} ${sections.filters ?? nothing}
      ${sections.sort ?? nothing}
    </div>
  `;
}

// ---- Batch-migration helpers (post-pilot wave over the remaining views) ----

/** Read a possibly-nested path ("Details.Owner") off an item. */
export function pathText(item: unknown, path: string): string {
  let v: unknown = item;
  for (const key of path.split('.')) {
    if (v == null) return '';
    v = (v as Record<string, unknown>)[key];
  }
  return v == null ? '' : String(v);
}

/**
 * Reuse a view's existing imperative cell renderer inside a list-row slot:
 * the renderer is invoked against a detached span with a minimal
 * GridItemModel-shaped `{ item }` (the pilot established the pattern —
 * edit-comments-controls et al. read only `model.item`; views whose renderer
 * reads column properties pass a colStub).
 */
export function hostCell<T>(
  host: Record<string, unknown> & HTMLElement,
  rendererName: string,
  colStub: object = {}
): (item: T) => TemplateResult {
  return item => {
    const root = document.createElement('span');
    const renderer = host[rendererName] as (
      root: HTMLElement,
      column: object,
      model: { item: T }
    ) => void;
    renderer.call(host, root, colStub, { item });
    return html`${root}`;
  };
}

/** Chip from a status-ish path with success/failure value sets. */
export function chipFromPath<T>(
  path: string,
  success: string[] = ['Complete', 'Running', 'Enabled', 'true', 'True'],
  failure: string[] = ['Failed', 'Errored', 'Cancelled', 'Stopped']
): (item: T) => { label: string; kind: 'success' | 'failure' | 'neutral' } | null {
  return item => {
    const label = pathText(item, path);
    if (!label) return null;
    const kind = success.includes(label)
      ? ('success' as const)
      : failure.includes(label)
        ? ('failure' as const)
        : ('neutral' as const);
    return { label, kind };
  };
}

/** Details panel from labelled paths. */
export function pathDetails<T>(
  fields: Array<{ label: string; path: string }>
): (item: T) => TemplateResult {
  return item =>
    renderListDetails(
      fields.map(f => ({ label: f.label, value: pathText(item, f.path) || '—' }))
    );
}

/**
 * One-call wiring for a migrated view: returns the list column's renderers.
 * Templates/bars are provided lazily so class-field initialisation order in
 * the host does not matter.
 */
export function narrowListRenderers<T>(
  getTemplate: () => ListRowTemplate<T>,
  getBar?: () => Parameters<typeof renderListBar>[0]
): {
  row: (root: HTMLElement, column: object, model: { item: T }) => void;
  bar: (root: HTMLElement) => void;
} {
  return {
    row: (root, _column, model) => {
      litRender(renderListRow(getTemplate(), model.item), root);
    },
    bar: root => {
      litRender(renderListBar(getBar?.() ?? {}), root);
    }
  };
}
