import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import '@vaadin/tooltip';
import { Notification } from '@vaadin/notification';
import { css, html, nothing } from 'lit';
import { customElement, query, state } from 'lit/decorators.js';
import { TerraformApi, TerraformTemplateManifest } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import '../components/deploy-from-template-dialog';
import { DeployFromTemplateDialog } from '../components/deploy-from-template-dialog';

type DetailTab = 'inputs' | 'outputs' | 'code';

const ALL_CATEGORIES = 'All';
const UNCATEGORISED = 'Other';

/**
 * Icon + tint per manifest category. Falls back to a neutral puzzle piece for
 * categories the catalog has not seen before, so a new manifest never renders
 * without an identity.
 */
const CATEGORY_ICONS: Record<string, { icon: string; tone: string }> = {
  data: { icon: 'vaadin:database', tone: 'primary' },
  storage: { icon: 'vaadin:storage', tone: 'success' },
  network: { icon: 'vaadin:connect', tone: 'warning' },
  compute: { icon: 'vaadin:server', tone: 'contrast' }
};

const categoryVisual = (category?: string | null) =>
  CATEGORY_ICONS[(category ?? '').toLowerCase()] ?? {
    icon: 'vaadin:puzzle-piece',
    tone: 'contrast'
  };

/** Builds the two "use in code" snippets without inheriting the source file's indentation. */
export const catalogReference = (t: TerraformTemplateManifest) =>
  [
    'TerraformSourceType = Catalog',
    `TerraformTemplateName = ${t.Name}`,
    `TerraformTemplateVersion = ${t.Version}`
  ].join('\n');

export const moduleSourceSnippet = (t: TerraformTemplateManifest) =>
  [
    `module "${t.Name}" {`,
    `  source = "git::${t.Source.Locator}//${t.Source.SubPath || `stock-modules/${t.Name}`}?ref=${t.Source.Ref}"`,
    '',
    '  # ... module inputs ...',
    '}'
  ].join('\n');

@customElement('page-stock-modules')
export class PageStockModules extends PageElement {
  static get styles() {
    return css`
      :host {
        display: block;
        height: calc(100vh - var(--dorc-header-height, 50px));
        overflow-y: auto;
        box-sizing: border-box;
        background: var(--dorc-bg-secondary);
        color: var(--dorc-text-primary);
      }
      .hero {
        background: var(--dorc-bg-primary);
        border-bottom: 1px solid var(--dorc-border-color);
        padding: var(--lumo-space-xl) var(--lumo-space-xl) var(--lumo-space-l);
      }
      .hero-inner,
      .body {
        max-width: 1184px;
        margin: 0 auto;
      }
      .hero-inner {
        display: flex;
        flex-wrap: wrap;
        gap: var(--lumo-space-l);
        align-items: flex-end;
        justify-content: space-between;
      }
      .hero-text {
        flex: 1 1 480px;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: var(--lumo-space-s);
      }
      .eyebrow {
        font-size: var(--lumo-font-size-xs);
        font-weight: 700;
        letter-spacing: 0.12em;
        text-transform: uppercase;
        color: var(--dorc-icon-interactive);
      }
      h1 {
        margin: 0;
        font-size: 2.1rem;
        font-weight: 700;
        letter-spacing: -0.02em;
        line-height: 1.1;
      }
      .lead {
        margin: 0;
        max-width: 620px;
        color: var(--dorc-text-secondary-strong);
        line-height: 1.5;
      }
      .stats {
        display: flex;
        gap: var(--lumo-space-s);
        flex-wrap: wrap;
      }
      .stat {
        display: flex;
        flex-direction: column;
        gap: 2px;
        padding: var(--lumo-space-s) var(--lumo-space-m);
        background: var(--dorc-bg-secondary);
        border: 1px solid var(--dorc-border-color);
        border-radius: var(--lumo-border-radius-l);
        min-width: 112px;
      }
      .stat-label {
        font-size: var(--lumo-font-size-xs);
        font-weight: 600;
        color: var(--dorc-text-secondary-strong);
      }
      .stat-value {
        font-size: var(--lumo-font-size-xxl);
        font-weight: 700;
        line-height: 1.1;
      }
      .stat-value.small {
        font-size: var(--lumo-font-size-l);
      }
      .body {
        padding: var(--lumo-space-l) var(--lumo-space-xl) var(--lumo-space-xl);
        display: flex;
        flex-direction: column;
        gap: var(--lumo-space-l);
      }
      .toolbar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--lumo-space-s);
      }
      .toolbar vaadin-text-field {
        flex: 1 1 320px;
        min-width: 200px;
      }
      .chips {
        display: flex;
        flex-wrap: wrap;
        gap: var(--lumo-space-xs);
      }
      .chip {
        border-radius: 999px;
        --lumo-button-size: 36px;
        font-weight: 500;
        background: var(--dorc-bg-primary);
        border: 1px solid var(--dorc-border-color);
        color: var(--dorc-text-primary);
        cursor: pointer;
      }
      .chip[aria-pressed='true'] {
        background: var(--lumo-primary-color);
        border-color: var(--lumo-primary-color);
        color: var(--lumo-primary-contrast-color);
      }
      .refresh {
        margin-left: auto;
      }
      .cards {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(min(100%, 440px), 1fr));
        gap: var(--lumo-space-m);
      }
      .card {
        background: var(--dorc-bg-primary);
        border: 1px solid var(--dorc-border-color);
        border-radius: var(--lumo-border-radius-l);
        padding: var(--lumo-space-l);
        display: flex;
        flex-direction: column;
        gap: var(--lumo-space-m);
        box-shadow: 0 1px 2px rgba(16, 24, 40, 0.04);
        transition:
          box-shadow 120ms ease,
          border-color 120ms ease;
      }
      .card:hover,
      .card[data-selected='true'] {
        border-color: var(--dorc-link-color);
        box-shadow: 0 6px 20px rgba(16, 24, 40, 0.08);
      }
      .card-head {
        display: flex;
        gap: var(--lumo-space-m);
        align-items: flex-start;
      }
      .icon-tile {
        width: 52px;
        height: 52px;
        border-radius: var(--lumo-border-radius-m);
        display: flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
      }
      .icon-tile.small {
        width: 36px;
        height: 36px;
      }
      .icon-tile vaadin-icon {
        width: 26px;
        height: 26px;
      }
      .icon-tile.small vaadin-icon {
        width: 18px;
        height: 18px;
      }
      .tone-primary {
        background: var(--lumo-primary-color-10pct);
        color: var(--lumo-primary-text-color);
      }
      .tone-success {
        background: var(--lumo-success-color-10pct);
        color: var(--lumo-success-text-color);
      }
      .tone-warning {
        background: var(--lumo-warning-color-10pct);
        color: var(--lumo-warning-text-color);
      }
      .tone-contrast {
        background: var(--lumo-contrast-10pct);
        color: var(--lumo-secondary-text-color);
      }
      .card-title {
        flex: 1;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: 4px;
      }
      .title-row {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-s);
        flex-wrap: wrap;
      }
      h2 {
        margin: 0;
        font-size: var(--lumo-font-size-xl);
        font-weight: 700;
        letter-spacing: -0.01em;
        overflow-wrap: anywhere;
      }
      .meta {
        font-size: var(--lumo-font-size-s);
        font-weight: 600;
        color: var(--dorc-text-secondary-strong);
      }
      .pill {
        font-family: var(--dorc-mono-font, ui-monospace, Menlo, monospace);
        font-size: var(--lumo-font-size-xs);
        font-weight: 600;
        color: var(--lumo-primary-text-color);
        background: var(--lumo-primary-color-10pct);
        border-radius: var(--lumo-border-radius-s);
        padding: 2px 8px;
      }
      .badge {
        font-size: var(--lumo-font-size-xs);
        font-weight: 600;
        border-radius: 999px;
        padding: 2px 10px;
        white-space: nowrap;
      }
      .badge-active {
        background: var(--lumo-success-color-10pct);
        color: var(--lumo-success-text-color);
      }
      .badge-deprecated {
        background: var(--lumo-error-color-10pct);
        color: var(--lumo-error-text-color);
      }
      .badge-required {
        background: var(--lumo-error-color-10pct);
        color: var(--lumo-error-text-color);
        font-size: 10px;
        letter-spacing: 0.06em;
      }
      .badge-sensitive {
        background: var(--dorc-warning-bg);
        color: var(--dorc-warning-text);
        font-size: 10px;
        letter-spacing: 0.06em;
        display: inline-flex;
        align-items: center;
        gap: 4px;
      }
      .badge-sensitive vaadin-icon {
        width: 11px;
        height: 11px;
      }
      .description {
        margin: 0;
        line-height: 1.5;
      }
      .tags {
        display: flex;
        gap: 6px;
        flex-wrap: wrap;
      }
      .tag {
        font-size: var(--lumo-font-size-xs);
        background: var(--dorc-bg-secondary);
        border: 1px solid var(--dorc-border-color);
        border-radius: 999px;
        padding: 2px 10px;
      }
      .facts {
        display: grid;
        grid-template-columns: repeat(4, minmax(0, 1fr));
        gap: var(--lumo-space-s);
        padding: var(--lumo-space-s) 0;
        border-top: 1px solid var(--dorc-border-color);
        border-bottom: 1px solid var(--dorc-border-color);
      }
      .fact-label {
        display: block;
        font-size: 11px;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--dorc-text-secondary-strong);
      }
      .fact-value {
        font-size: var(--lumo-font-size-m);
        font-weight: 700;
        display: inline-flex;
        align-items: center;
        gap: 6px;
      }
      .fact-value.mono {
        font-family: var(--dorc-mono-font, ui-monospace, Menlo, monospace);
        font-size: var(--lumo-font-size-s);
      }
      .fact-sub {
        font-size: var(--lumo-font-size-xs);
        font-weight: 500;
        color: var(--dorc-text-secondary-strong);
      }
      .card-actions {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--lumo-space-s);
        flex-wrap: wrap;
      }
      .deprecation {
        margin: 0;
        padding: var(--lumo-space-s) var(--lumo-space-m);
        border-radius: var(--lumo-border-radius-m);
        background: var(--lumo-error-color-10pct);
        color: var(--lumo-error-text-color);
        font-size: var(--lumo-font-size-s);
      }
      .detail {
        background: var(--dorc-bg-primary);
        border: 1px solid var(--dorc-border-color);
        border-radius: var(--lumo-border-radius-l);
        overflow: hidden;
        scroll-margin-top: var(--lumo-space-m);
      }
      .detail-head {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-m);
        padding: var(--lumo-space-m) var(--lumo-space-l);
        border-bottom: 1px solid var(--dorc-border-color);
        flex-wrap: wrap;
      }
      .detail-name {
        font-weight: 700;
        font-size: var(--lumo-font-size-l);
      }
      .detail-name span {
        font-family: var(--dorc-mono-font, ui-monospace, Menlo, monospace);
        font-size: var(--lumo-font-size-s);
        font-weight: 500;
        color: var(--dorc-text-secondary-strong);
        margin-left: 6px;
      }
      .segmented {
        display: flex;
        gap: 4px;
        background: var(--dorc-bg-secondary);
        border-radius: var(--lumo-border-radius-m);
        padding: 4px;
      }
      .segmented vaadin-button {
        --lumo-button-size: 32px;
        border-radius: var(--lumo-border-radius-s);
        color: var(--dorc-text-secondary-strong);
        background: transparent;
      }
      .segmented vaadin-button[aria-pressed='true'] {
        background: var(--dorc-bg-primary);
        color: var(--dorc-text-primary);
        box-shadow: 0 1px 2px rgba(16, 24, 40, 0.12);
      }
      .detail-tools {
        margin-left: auto;
        display: flex;
        gap: var(--lumo-space-xs);
      }
      .table-wrap {
        overflow-x: auto;
      }
      table {
        width: 100%;
        border-collapse: collapse;
        font-size: var(--lumo-font-size-s);
        min-width: 640px;
      }
      th {
        text-align: left;
        padding: 10px var(--lumo-space-m);
        font-size: 11px;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--dorc-text-secondary-strong);
        font-weight: 700;
        background: var(--dorc-bg-secondary);
      }
      td {
        padding: 12px var(--lumo-space-m);
        border-top: 1px solid var(--dorc-border-color);
        vertical-align: top;
      }
      td.name {
        font-family: var(--dorc-mono-font, ui-monospace, Menlo, monospace);
        font-weight: 600;
        white-space: nowrap;
      }
      td.name .badge {
        margin-left: 6px;
        vertical-align: middle;
      }
      td.muted,
      .muted {
        color: var(--dorc-text-secondary-strong);
      }
      td.mono {
        font-family: var(--dorc-mono-font, ui-monospace, Menlo, monospace);
        font-size: var(--lumo-font-size-xs);
      }
      .code-section {
        padding: var(--lumo-space-m) var(--lumo-space-l);
        display: flex;
        flex-direction: column;
        gap: var(--lumo-space-m);
      }
      .code-section h3 {
        margin: 0 0 var(--lumo-space-xs);
        font-size: var(--lumo-font-size-s);
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--dorc-text-secondary-strong);
      }
      pre {
        margin: 0;
        padding: var(--lumo-space-m);
        background: var(--dorc-bg-secondary);
        border: 1px solid var(--dorc-border-color);
        border-radius: var(--lumo-border-radius-m);
        font-family: var(--dorc-mono-font, ui-monospace, Menlo, monospace);
        font-size: var(--lumo-font-size-s);
        overflow-x: auto;
      }
      .requirements {
        display: flex;
        gap: var(--lumo-space-l);
        flex-wrap: wrap;
        padding: var(--lumo-space-m) var(--lumo-space-l);
        border-top: 1px solid var(--dorc-border-color);
        font-size: var(--lumo-font-size-s);
      }
      .state {
        background: var(--dorc-bg-primary);
        border: 1px dashed var(--dorc-border-color);
        border-radius: var(--lumo-border-radius-l);
        padding: var(--lumo-space-xl);
        text-align: center;
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--lumo-space-s);
        color: var(--dorc-text-secondary-strong);
      }
      .state vaadin-icon {
        width: 36px;
        height: 36px;
      }
      .state.error {
        border-color: var(--lumo-error-color-50pct);
        color: var(--lumo-error-text-color);
      }
      .skeleton {
        height: 280px;
        border-radius: var(--lumo-border-radius-l);
        background: linear-gradient(
          90deg,
          var(--lumo-contrast-5pct) 25%,
          var(--lumo-contrast-10pct) 37%,
          var(--lumo-contrast-5pct) 63%
        );
        background-size: 400% 100%;
        animation: shimmer 1.4s ease infinite;
      }
      @keyframes shimmer {
        0% {
          background-position: 100% 0;
        }
        100% {
          background-position: 0 0;
        }
      }
      @media (prefers-reduced-motion: reduce) {
        .skeleton {
          animation: none;
        }
      }
      @media (max-width: 768px) {
        .hero,
        .body {
          padding-left: var(--lumo-space-m);
          padding-right: var(--lumo-space-m);
        }
        h1 {
          font-size: 1.6rem;
        }
        .facts {
          grid-template-columns: repeat(2, minmax(0, 1fr));
        }
      }
    `;
  }

  @state() private templates: TerraformTemplateManifest[] = [];
  @state() private loading = false;
  @state() private error: string | null = null;
  @state() private search = '';
  @state() private category = ALL_CATEGORIES;
  @state() private showDeprecated = false;
  @state() private detail: TerraformTemplateManifest | null = null;
  @state() private detailTab: DetailTab = 'inputs';

  @query('deploy-from-template-dialog')
  private deployDialog!: DeployFromTemplateDialog;

  @query('.detail')
  private detailPanel?: HTMLElement;

  private terraformApi = new TerraformApi(dorcApiConfiguration);

  connectedCallback() {
    super.connectedCallback();
    this.loadTemplates();
  }

  private loadTemplates() {
    this.loading = true;
    this.error = null;
    this.terraformApi.terraformTemplatesGet().subscribe({
      next: data => {
        this.templates = data ?? [];
        this.loading = false;
      },
      error: err => {
        this.error =
          retrieveErrorMessage(err) ?? 'Failed to load stock modules.';
        this.loading = false;
      }
    });
  }

  private get categories(): string[] {
    const names = new Set(
      this.templates.map(t => t.Category?.trim() || UNCATEGORISED)
    );
    return [...names].sort((a, b) => a.localeCompare(b));
  }

  private get deprecatedCount() {
    return this.templates.filter(t => t.Deprecated).length;
  }

  private get providerNames(): string[] {
    const names = new Set<string>();
    for (const t of this.templates) {
      for (const provider of Object.keys(t.RequiredProviders ?? {})) {
        names.add(provider);
      }
    }
    return [...names].sort((a, b) => a.localeCompare(b));
  }

  get filteredTemplates() {
    const query = this.search.trim().toLowerCase();
    return this.templates.filter(
      t =>
        (this.showDeprecated || !t.Deprecated) &&
        (this.category === ALL_CATEGORIES ||
          (t.Category?.trim() || UNCATEGORISED) === this.category) &&
        [t.Name, t.Version, t.Category, t.Description, ...(t.Tags ?? [])].some(
          value => value?.toLowerCase().includes(query)
        )
    );
  }

  render() {
    return html`
      <header class="hero">
        <div class="hero-inner">
          <div class="hero-text">
            <div class="eyebrow">Infrastructure</div>
            <h1>Stock modules</h1>
            <p class="lead">
              Curated, versioned Terraform modules you can plan into any mapped
              environment. Pick a module, choose a target, fill the inputs, and
              review the plan before anything is applied.
            </p>
          </div>
          <div class="stats" aria-label="Catalog summary">
            <div class="stat">
              <span class="stat-label">Modules</span>
              <span class="stat-value"
                >${this.loading ? '…' : this.templates.length}</span
              >
            </div>
            <div class="stat">
              <span class="stat-label">Categories</span>
              <span class="stat-value"
                >${this.loading ? '…' : this.categories.length}</span
              >
            </div>
            <div class="stat">
              <span class="stat-label">Providers</span>
              <span class="stat-value small"
                >${this.loading ? '…' : this.providerNames.join(', ') || '—'}</span
              >
            </div>
          </div>
        </div>
      </header>

      <div class="body">
        <div class="toolbar">
          <vaadin-text-field
            placeholder="Search by name, tag or description"
            aria-label="Search modules"
            .value=${this.search}
            @value-changed=${(e: CustomEvent<{ value: string }>) => (this.search = e.detail.value ?? '')}
            clear-button-visible
          >
            <vaadin-icon slot="prefix" icon="vaadin:search"></vaadin-icon>
          </vaadin-text-field>
          <div class="chips" role="group" aria-label="Filter by category">
            ${[ALL_CATEGORIES, ...this.categories].map(
              c => html`
                <vaadin-button
                  class="chip"
                  theme="small"
                  aria-pressed=${this.category === c ? 'true' : 'false'}
                  @click=${() => (this.category = c)}
                  >${c}</vaadin-button
                >
              `
            )}
            <vaadin-button
              class="chip"
              theme="small"
              aria-pressed=${this.showDeprecated ? 'true' : 'false'}
              @click=${() => (this.showDeprecated = !this.showDeprecated)}
              >Deprecated (${this.deprecatedCount})</vaadin-button
            >
          </div>
          <vaadin-button
            class="refresh"
            theme="icon"
            aria-label="Refresh catalog"
            @click=${() => this.loadTemplates()}
            .disabled=${this.loading}
          >
            <vaadin-tooltip
              slot="tooltip"
              text="Refresh catalog"
            ></vaadin-tooltip>
            <vaadin-icon icon="vaadin:refresh"></vaadin-icon>
          </vaadin-button>
        </div>

        ${this.renderContent()}
      </div>

      <deploy-from-template-dialog></deploy-from-template-dialog>
    `;
  }

  private renderContent() {
    if (this.loading) {
      return html`
        <div class="cards" role="status" aria-label="Loading Terraform modules">
          <div class="skeleton"></div>
          <div class="skeleton"></div>
        </div>
      `;
    }
    if (this.error) {
      return html`
        <div class="state error" role="alert">
          <vaadin-icon icon="vaadin:warning"></vaadin-icon>
          <strong>Could not load the catalog</strong>
          <span>${this.error}</span>
          <vaadin-button theme="tertiary" @click=${() => this.loadTemplates()}
            >Try again</vaadin-button
          >
        </div>
      `;
    }
    const items = this.filteredTemplates;
    if (!items.length) {
      return html`
        <div class="state" role="status">
          <vaadin-icon icon="vaadin:puzzle-piece"></vaadin-icon>
          <strong
            >${this.templates.length ? 'No modules match these filters' : 'The catalog is empty'}</strong
          >
          <span
            >${this.templates.length ? 'Clear the search or pick another category.' : 'Stock modules are published by an administrator from the manifests directory.'}</span
          >
        </div>
      `;
    }
    return html`
      <div class="cards">${items.map(t => this.renderCard(t))}</div>
      ${this.detail ? this.renderDetail(this.detail) : nothing}
    `;
  }

  private renderCard(t: TerraformTemplateManifest) {
    const visual = categoryVisual(t.Category);
    const params = t.Parameters ?? [];
    const required = params.filter(p => p.Required).length;
    const secrets = params.filter(p => p.Sensitive).length;
    const selected = this.isSameTemplate(this.detail, t);
    return html`
      <article class="card" data-selected=${selected ? 'true' : 'false'}>
        <div class="card-head">
          <div class="icon-tile tone-${visual.tone}">
            <vaadin-icon icon=${visual.icon}></vaadin-icon>
          </div>
          <div class="card-title">
            <div class="title-row">
              <h2>${t.Name}</h2>
              <span class="pill">v${t.Version}</span>
              ${
                t.Deprecated
                  ? html`<span class="badge badge-deprecated">Deprecated</span>`
                  : html`<span class="badge badge-active">Active</span>`
              }
            </div>
            <div class="meta">
              ${t.Category?.trim() || UNCATEGORISED}${t.Owner ? ` · ${t.Owner}` : ''}
            </div>
          </div>
        </div>
        <p class="description">
          ${t.Description ?? 'No description provided.'}
        </p>
        ${
          t.Deprecated && t.DeprecationReason
            ? html`<p class="deprecation">${t.DeprecationReason}</p>`
            : nothing
        }
        ${
          t.Tags?.length
            ? html`<div class="tags">
                ${t.Tags.map(tag => html`<span class="tag">${tag}</span>`)}
              </div>`
            : nothing
        }
        <div class="facts">
          <div>
            <span class="fact-label">Inputs</span>
            <span class="fact-value"
              >${params.length}
              <span class="fact-sub">· ${required} required</span></span
            >
          </div>
          <div>
            <span class="fact-label">Outputs</span>
            <span class="fact-value">${t.Outputs?.length ?? 0}</span>
          </div>
          <div>
            <span class="fact-label">Secrets</span>
            <span class="fact-value ${secrets ? '' : 'muted'}">
              ${
                secrets
                  ? html`<vaadin-icon
                      icon="vaadin:lock"
                      style="width:14px;height:14px;color:var(--dorc-warning-text)"
                    ></vaadin-icon>`
                  : nothing
              }
              ${secrets}
            </span>
          </div>
          <div>
            <span class="fact-label">Terraform</span>
            <span class="fact-value mono">${t.RequiredTerraformVersion}</span>
          </div>
        </div>
        <div class="card-actions">
          <vaadin-button
            theme="tertiary"
            aria-pressed=${selected ? 'true' : 'false'}
            @click=${() => this.showDetail(t)}
          >
            View inputs &amp; outputs
            <vaadin-icon icon="vaadin:angle-right" slot="suffix"></vaadin-icon>
          </vaadin-button>
          <vaadin-button theme="primary" @click=${() => this.openDeploy(t)}>
            <vaadin-icon icon="vaadin:play" slot="prefix"></vaadin-icon>
            Plan deployment
          </vaadin-button>
        </div>
      </article>
    `;
  }

  private renderDetail(t: TerraformTemplateManifest) {
    const visual = categoryVisual(t.Category);
    const tab = (id: DetailTab, label: string) => html`
      <vaadin-button
        theme="small"
        aria-pressed=${this.detailTab === id ? 'true' : 'false'}
        @click=${() => (this.detailTab = id)}
        >${label}</vaadin-button
      >
    `;
    return html`
      <section class="detail" aria-label="${t.Name} ${t.Version} details">
        <div class="detail-head">
          <div class="icon-tile small tone-${visual.tone}">
            <vaadin-icon icon=${visual.icon}></vaadin-icon>
          </div>
          <div class="detail-name">${t.Name}<span>v${t.Version}</span></div>
          <div class="segmented" role="group" aria-label="Module details">
            ${tab('inputs', `Inputs (${t.Parameters?.length ?? 0})`)}
            ${tab('outputs', `Outputs (${t.Outputs?.length ?? 0})`)}
            ${tab('code', 'Use in code')}
          </div>
          <div class="detail-tools">
            <vaadin-button theme="small" @click=${() => this.copyReference(t)}>
              <vaadin-icon icon="vaadin:copy" slot="prefix"></vaadin-icon>
              Copy reference
            </vaadin-button>
            <vaadin-button
              theme="icon small tertiary"
              aria-label="Close details"
              @click=${() => (this.detail = null)}
            >
              <vaadin-icon icon="vaadin:close"></vaadin-icon>
            </vaadin-button>
          </div>
        </div>
        ${
          this.detailTab === 'inputs'
            ? this.renderInputs(t)
            : this.detailTab === 'outputs'
              ? this.renderOutputs(t)
              : this.renderCode(t)
        }
        <div class="requirements">
          <span
            ><span class="muted">Terraform</span>
            ${t.RequiredTerraformVersion}</span
          >
          ${Object.entries(t.RequiredProviders ?? {}).map(
            ([k, v]) => html`<span><span class="muted">${k}</span> ${v}</span>`
          )}
          ${t.Owner ? html`<span><span class="muted">Owner</span> ${t.Owner}</span>` : nothing}
        </div>
      </section>
    `;
  }

  private renderInputs(t: TerraformTemplateManifest) {
    return html`
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Input</th>
              <th>Type</th>
              <th>Default</th>
              <th>Constraint</th>
              <th>Description</th>
            </tr>
          </thead>
          <tbody>
            ${(t.Parameters ?? []).map(
              p => html`
                <tr>
                  <td class="name">
                    ${p.Name}
                    ${p.Required ? html`<span class="badge badge-required">REQUIRED</span>` : nothing}
                    ${
                      p.Sensitive
                        ? html`<span class="badge badge-sensitive"
                            ><vaadin-icon icon="vaadin:lock"></vaadin-icon
                            >SENSITIVE</span
                          >`
                        : nothing
                    }
                  </td>
                  <td class="muted">${p.Type}</td>
                  <td class="mono">
                    ${p.Sensitive ? html`<span class="muted">never defaulted</span>` : (p.Default ?? '—')}
                  </td>
                  <td class="muted">${this.constraintText(p)}</td>
                  <td>${p.Description ?? ''}</td>
                </tr>
              `
            )}
          </tbody>
        </table>
      </div>
    `;
  }

  private renderOutputs(t: TerraformTemplateManifest) {
    return html`
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Output</th>
              <th>Type</th>
              <th>Sensitive</th>
              <th>Description</th>
            </tr>
          </thead>
          <tbody>
            ${(t.Outputs ?? []).map(
              o => html`
                <tr>
                  <td class="name">${o.Name}</td>
                  <td class="muted">${o.Type}</td>
                  <td>
                    ${
                      o.Sensitive
                        ? html`<span class="badge badge-sensitive"
                            ><vaadin-icon icon="vaadin:lock"></vaadin-icon
                            >SENSITIVE</span
                          >`
                        : html`<span class="muted">no</span>`
                    }
                  </td>
                  <td>${o.Description ?? ''}</td>
                </tr>
              `
            )}
          </tbody>
        </table>
      </div>
    `;
  }

  private renderCode(t: TerraformTemplateManifest) {
    return html`
      <div class="code-section">
        <div>
          <h3>Consume from a DOrc component</h3>
          <pre>${catalogReference(t)}</pre>
        </div>
        <div>
          <h3>Reference the module from your own Terraform</h3>
          <pre>${moduleSourceSnippet(t)}</pre>
        </div>
        <div class="muted">
          Source: ${t.Source.Kind} · ${t.Source.Locator} @ ${t.Source.Ref}
        </div>
      </div>
    `;
  }

  private constraintText(p: TerraformTemplateManifest['Parameters'][number]) {
    const parts: string[] = [];
    if (p.AllowedValues?.length)
      parts.push(`${p.AllowedValues.length} allowed values`);
    if (p.Pattern) parts.push(p.Pattern);
    if (p.Min != null || p.Max != null) {
      parts.push(
        p.Min != null && p.Max != null
          ? `${p.Min} to ${p.Max}`
          : p.Min != null
            ? `at least ${p.Min}`
            : `at most ${p.Max}`
      );
    }
    return parts.length ? parts.join(' · ') : '—';
  }

  private isSameTemplate(
    a: TerraformTemplateManifest | null,
    b: TerraformTemplateManifest
  ) {
    return !!a && a.Name === b.Name && a.Version === b.Version;
  }

  private async showDetail(t: TerraformTemplateManifest) {
    this.detail = t;
    await this.updateComplete;
    this.detailPanel?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  private openDeploy(t: TerraformTemplateManifest) {
    const context = new URLSearchParams(window.location.search);
    this.deployDialog.open(t, {
      projectName: context.get('project') ?? undefined,
      environmentName: context.get('environment') ?? undefined
    });
  }

  private copyReference(t: TerraformTemplateManifest) {
    navigator.clipboard.writeText(catalogReference(t)).then(
      () => {
        const n = Notification.show(
          `Copied catalog reference for ${t.Name}@${t.Version}`,
          { duration: 2000, position: 'bottom-end' }
        );
        n.setAttribute('theme', 'success');
      },
      () => {
        Notification.show('Clipboard copy failed.', {
          duration: 2000,
          position: 'bottom-end'
        });
      }
    );
  }
}
