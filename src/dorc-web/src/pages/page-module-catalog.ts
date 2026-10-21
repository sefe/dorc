import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import '@vaadin/tooltip';
import { Notification } from '@vaadin/notification';
import { css, html, nothing, svg } from 'lit';
import { customElement, query, state } from 'lit/decorators.js';
import {
  ComponentType,
  DeploymentDefinitionsApi,
  TerraformApi,
  TerraformTemplateManifest,
  type DeploymentDefinitionApiModel
} from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import {
  GENERIC_SCRIPT_TEMPLATES,
  serializeScriptPathValue
} from '../helpers/generic-scripts';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import '../components/deploy-from-template-dialog';
import { BRAND_ICON_PATHS, BrandIcon } from '../icons/brand-icons';
import { DeployFromTemplateDialog } from '../components/deploy-from-template-dialog';

type DetailTab = 'inputs' | 'outputs' | 'code';

const ALL_CATEGORIES = 'All';
const UNCATEGORISED = 'Other';
const SCRIPTS_CATEGORY = 'PowerShell Scripts';

/**
 * One card per module name; the catalog may ship several versions of the same
 * module and users pick between them with the card's version selector.
 */
export interface ModuleGroup {
  name: string;
  /** All published versions, newest first. */
  versions: TerraformTemplateManifest[];
  /** The manifest for the version the user currently has selected. */
  manifest: TerraformTemplateManifest;
}

/**
 * Descending semver-ish comparison. Catalog versions are plain dotted
 * numerics, but fall back to string comparison for anything unexpected so
 * sorting never throws.
 */
export const compareVersionsDesc = (a: string, b: string): number => {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  if (pa.some(Number.isNaN) || pb.some(Number.isNaN)) {
    return b.localeCompare(a);
  }
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const diff = (pb[i] ?? 0) - (pa[i] ?? 0);
    if (diff !== 0) return diff;
  }
  return 0;
};

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

/**
 * Tint per brand mark so modules show the toolset they actually provision
 * instead of a generic category glyph.
 */
const BRAND_TONES: Record<BrandIcon, string> = {
  kafka: 'contrast',
  clickhouse: 'warning',
  postgresql: 'primary',
  azure: 'primary',
  terraform: 'contrast'
};

const brandIconSvg = (key: BrandIcon) => svg`<svg
  class="brand-icon"
  viewBox="0 0 24 24"
  aria-hidden="true"
><path d=${BRAND_ICON_PATHS[key]}></path></svg>`;

/**
 * Picks the toolset's actual brand mark from the module's tags and required
 * providers, falling back to the category glyph when no brand matches.
 */
const moduleVisual = (t: TerraformTemplateManifest) => {
  const tags = (t.Tags ?? []).map(tag => tag.toLowerCase());
  const providers = Object.keys(t.RequiredProviders ?? {}).map(p =>
    p.toLowerCase()
  );
  let brand: BrandIcon | undefined;
  if (tags.includes('kafka')) brand = 'kafka';
  else if (tags.includes('clickhouse')) brand = 'clickhouse';
  else if (tags.includes('postgresql') || tags.includes('postgres'))
    brand = 'postgresql';
  else if (tags.includes('azure') || providers.includes('azurerm'))
    brand = 'azure';
  else if (providers.length) brand = 'terraform';
  if (brand) {
    return { tone: BRAND_TONES[brand], body: brandIconSvg(brand) };
  }
  const fallback = categoryVisual(t.Category);
  return {
    tone: fallback.tone,
    body: html`<vaadin-icon icon=${fallback.icon}></vaadin-icon>`
  };
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

@customElement('page-module-catalog')
export class PageModuleCatalog extends PageElement {
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
        padding: var(--lumo-space-m) var(--lumo-space-l);
      }
      .hero-inner,
      .body {
        max-width: 1184px;
        margin: 0 auto;
      }
      .hero-inner {
        display: flex;
        flex-wrap: wrap;
        gap: var(--lumo-space-m);
        align-items: center;
        justify-content: space-between;
      }
      .hero-text {
        flex: 1 1 420px;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: var(--lumo-space-xs);
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
        font-size: var(--lumo-font-size-xl);
        font-weight: 700;
        letter-spacing: -0.02em;
        line-height: 1.15;
      }
      .lead {
        margin: 0;
        max-width: 620px;
        font-size: var(--lumo-font-size-s);
        color: var(--dorc-text-secondary-strong);
        line-height: 1.4;
      }
      .stats {
        display: flex;
        gap: var(--lumo-space-xs);
        flex-wrap: wrap;
      }
      .stat {
        display: flex;
        flex-direction: row;
        align-items: baseline;
        gap: var(--lumo-space-xs);
        padding: var(--lumo-space-xs) var(--lumo-space-s);
        background: var(--dorc-bg-secondary);
        border: 1px solid var(--dorc-border-color);
        border-radius: var(--lumo-border-radius-m);
      }
      .stat-label {
        font-size: var(--lumo-font-size-xs);
        font-weight: 600;
        color: var(--dorc-text-secondary-strong);
      }
      .stat-value {
        font-size: var(--lumo-font-size-m);
        font-weight: 700;
        line-height: 1.1;
      }
      .stat-value.small {
        font-size: var(--lumo-font-size-s);
      }
      .body {
        padding: var(--lumo-space-s) var(--lumo-space-m);
        display: flex;
        flex-direction: column;
        gap: var(--lumo-space-s);
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
        grid-template-columns: repeat(auto-fill, minmax(min(100%, 340px), 1fr));
        gap: var(--lumo-space-m);
      }
      .card {
        background: var(--dorc-bg-primary);
        border: 1px solid var(--dorc-border-color);
        border-radius: var(--lumo-border-radius-l);
        padding: var(--lumo-space-m);
        display: flex;
        flex-direction: column;
        gap: var(--lumo-space-s);
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
        gap: var(--lumo-space-s);
        align-items: flex-start;
      }
      .icon-tile {
        width: 40px;
        height: 40px;
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
        width: 22px;
        height: 22px;
      }
      .icon-tile.small vaadin-icon {
        width: 18px;
        height: 18px;
      }
      .icon-tile .brand-icon {
        width: 22px;
        height: 22px;
        fill: currentColor;
      }
      .icon-tile.small .brand-icon {
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
        font-size: var(--lumo-font-size-l);
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
      select.version-select {
        border: none;
        cursor: pointer;
        appearance: auto;
        padding: 2px 4px 2px 8px;
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
        /* Manifest descriptions can contain long dot-separated tokens
           (e.g. topic naming patterns) with no break opportunities. */
        overflow-wrap: anywhere;
      }
      .setup {
        margin: var(--lumo-space-l) 0 0;
        border: 1px solid var(--dorc-border-color);
        border-left: 4px solid var(--dorc-icon-interactive);
        border-radius: var(--lumo-border-radius-l);
        background: var(--dorc-bg-primary);
      }
      .setup summary {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-s);
        padding: var(--lumo-space-m) var(--lumo-space-l);
        cursor: pointer;
        font-weight: 600;
        list-style: none;
      }
      .setup summary::-webkit-details-marker {
        display: none;
      }
      .setup summary vaadin-icon {
        width: 18px;
        height: 18px;
        color: var(--dorc-icon-interactive);
        flex: none;
      }
      .setup summary .hint {
        margin-left: auto;
        font-weight: 400;
        font-size: var(--lumo-font-size-s);
        color: var(--dorc-text-secondary-strong);
      }
      .setup-body {
        padding: 0 var(--lumo-space-l) var(--lumo-space-l);
        display: grid;
        gap: var(--lumo-space-m);
        line-height: 1.5;
      }
      .setup-body h3 {
        margin: 0 0 var(--lumo-space-xs);
        font-size: var(--lumo-font-size-m);
      }
      .setup-body p {
        margin: 0;
        color: var(--dorc-text-secondary-strong);
      }
      .setup-body table {
        border-collapse: collapse;
        font-size: var(--lumo-font-size-s);
      }
      .setup-body td {
        padding: 4px 16px 4px 0;
        vertical-align: top;
      }
      .setup-body code {
        background: var(--dorc-bg-secondary);
        border: 1px solid var(--dorc-border-color);
        border-radius: var(--lumo-border-radius-s);
        padding: 1px 5px;
        font-size: 0.85em;
        white-space: nowrap;
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
        margin-top: auto;
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
        /* Rendered inside the cards grid directly after its card so the
           panel opens adjacent to what was clicked, spanning the full row. */
        grid-column: 1 / -1;
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
          padding: var(--lumo-space-s) var(--lumo-space-m);
        }
        .eyebrow,
        .lead {
          display: none;
        }
        h1 {
          font-size: var(--lumo-font-size-l);
        }
        .facts {
          grid-template-columns: repeat(2, minmax(0, 1fr));
        }
      }
    `;
  }

  @state() private templates: TerraformTemplateManifest[] = [];
  @state() private scriptDefinitions: DeploymentDefinitionApiModel[] = [];
  @state() private scriptsLoading = false;
  @state() private scriptsLoaded = false;
  @state() private loading = false;
  @state() private error: string | null = null;
  @state() private search = '';
  @state() private category = ALL_CATEGORIES;
  @state() private showDeprecated = false;
  /** Module name whose inline detail panel is open, if any. */
  @state() private detailName: string | null = null;
  @state() private detailTab: DetailTab = 'inputs';
  /** Per-module version picked in the card's selector; defaults to latest. */
  @state() private selectedVersions: Record<string, string> = {};

  @query('deploy-from-template-dialog')
  private deployDialog!: DeployFromTemplateDialog;

  @query('.detail')
  private detailPanel?: HTMLElement;

  private terraformApi = new TerraformApi(dorcApiConfiguration);
  private openedLinkedDefinition = false;

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
        const context = new URLSearchParams(window.location.search);
        const name = context.get('template');
        if (name && !this.openedLinkedDefinition) {
          this.openedLinkedDefinition = true;
          const template = this.templates.find(
            item =>
              item.Name === name && item.Version === context.get('version')
          );
          if (template) {
            void this.updateComplete.then(() => this.openDeploy(template));
          } else {
            this.error =
              'The linked module version is no longer available in the catalog. Historical deployment evidence remains available from the project definition.';
          }
        }
      },
      error: err => {
        this.error =
          retrieveErrorMessage(err) ?? 'Failed to load the module catalog.';
        this.loading = false;
      }
    });
  }

  /** All modules grouped by name with versions newest-first. */
  private get moduleGroups(): ModuleGroup[] {
    const byName = new Map<string, TerraformTemplateManifest[]>();
    for (const t of this.templates) {
      const list = byName.get(t.Name) ?? [];
      list.push(t);
      byName.set(t.Name, list);
    }
    return [...byName.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([name, versions]) => {
        versions.sort((a, b) => compareVersionsDesc(a.Version, b.Version));
        const picked =
          versions.find(v => v.Version === this.selectedVersions[name]) ??
          versions.find(v => !v.Deprecated) ??
          versions[0];
        return { name, versions, manifest: picked };
      });
  }

  private get categories(): string[] {
    const names = new Set(
      this.moduleGroups.map(g => g.manifest.Category?.trim() || UNCATEGORISED)
    );
    return [...names].sort((a, b) => a.localeCompare(b));
  }

  /** Modules every published version of which is deprecated. */
  private get deprecatedCount() {
    return this.moduleGroups.filter(g => g.versions.every(v => v.Deprecated))
      .length;
  }

  private selectCategory(category: string) {
    this.category = category;
    if (
      category === SCRIPTS_CATEGORY &&
      !this.scriptsLoaded &&
      !this.scriptsLoading
    ) {
      this.loadScriptCatalog();
    }
  }

  private loadScriptCatalog() {
    this.scriptsLoading = true;
    new DeploymentDefinitionsApi(dorcApiConfiguration)
      .deploymentDefinitionsGet({
        page: 1,
        limit: 100,
        componentType: ComponentType.PowerShell
      })
      .subscribe({
        next: response => {
          this.scriptsLoading = false;
          this.scriptsLoaded = true;
          const seen = new Set<string>();
          this.scriptDefinitions = (response.Items ?? []).filter(item => {
            const comp = item.Component;
            const key = `${comp?.ScriptPath ?? ''}|${comp?.ComponentName ?? ''}`;
            if (!comp?.ScriptPath || seen.has(key)) return false;
            seen.add(key);
            return true;
          });
        },
        error: () => {
          this.scriptsLoading = false;
          this.scriptsLoaded = true;
        }
      });
  }

  private get filteredScripts(): DeploymentDefinitionApiModel[] {
    const query = this.search.trim().toLowerCase();
    if (!query) return this.scriptDefinitions;
    return this.scriptDefinitions.filter(item =>
      [
        item.Component?.ComponentName,
        item.Component?.ScriptPath,
        item.Component?.PSVersion,
        item.ProjectName
      ].some(value => value?.toLowerCase().includes(query))
    );
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

  get filteredGroups(): ModuleGroup[] {
    const query = this.search.trim().toLowerCase();
    return this.moduleGroups.filter(
      g =>
        (this.showDeprecated || g.versions.some(v => !v.Deprecated)) &&
        (this.category === ALL_CATEGORIES ||
          (g.manifest.Category?.trim() || UNCATEGORISED) === this.category) &&
        g.versions.some(t =>
          [
            t.Name,
            t.Version,
            t.Category,
            t.Description,
            ...(t.Tags ?? [])
          ].some(value => value?.toLowerCase().includes(query))
        )
    );
  }

  render() {
    return html`
      <header class="hero">
        <div class="hero-inner">
          <div class="hero-text">
            <div class="eyebrow">Infrastructure</div>
            <h1>Module catalog</h1>
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
                >${this.loading ? '…' : this.moduleGroups.length}</span
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
        <details class="setup">
          <summary>
            <vaadin-icon icon="vaadin:key"></vaadin-icon>
            Before you deploy: set the target environment's cloud credentials
            <span class="hint"
              >Environment page &rsaquo; Cloud tab &amp; Properties</span
            >
          </summary>
          <div class="setup-body">
            <p>
              Each DOrc environment authenticates Terraform with its own
              credentials, configured as properties on the target environment.
              Secrets must be created as <strong>secure</strong> properties.
            </p>
            <div>
              <h3>Azure modules (azurerm)</h3>
              <p>
                Name the target subscription by attaching a cloud resource to
                the environment (Environment page &rsaquo; Cloud tab): resource
                type <code>Subscription</code> with the subscription GUID as the
                resource identifier. The legacy
                <code>TerraformSubscriptionId</code> property is used only when
                no subscription cloud resource is attached.
              </p>
              <p>
                Then set all three service-principal properties, or none to fall
                back to the runner host's ambient identity. The actual Terraform
                Azure identity needs workload RBAC on the target subscription
                and Storage Blob Data Contributor on the project's selected
                state container. Backend access uses Azure AD, not storage keys.
              </p>
              <table>
                <tr>
                  <td><code>TerraformClientId</code></td>
                  <td>Service principal application (client) ID GUID</td>
                </tr>
                <tr>
                  <td><code>TerraformClientSecret</code></td>
                  <td>Client secret — <strong>secure property</strong></td>
                </tr>
                <tr>
                  <td><code>TerraformTenantId</code></td>
                  <td>Entra tenant ID GUID</td>
                </tr>
              </table>
              <h3>Terraform state storage (all Catalog modules)</h3>
              <p>
                Open the destination project's
                <strong>Project Ref Data</strong>, then the
                <strong>Terraform State</strong> tab. Add a project default or a
                complete override for a mapped environment: state storage
                account, container, subscription ID and optional resource group.
                The account and container must already exist. This is separate
                from the workload subscription above; they may use the same
                subscription, but neither is inferred from the other.
              </p>
              <p>
                DOrc does not require a global state store. Requests pin the
                selected backend through plan and apply. Existing backend moves
                require an operator-reviewed state migration, not just a
                settings edit. Keep credentials in secure environment
                properties, not backend fields.
              </p>
            </div>
            <div>
              <h3>Aiven modules (ClickHouse, Kafka topics, PostgreSQL)</h3>
              <table>
                <tr>
                  <td><code>TerraformAivenApiToken</code></td>
                  <td>
                    Aiven API token — <strong>secure property</strong>, passed
                    to terraform as <code>AIVEN_TOKEN</code>
                  </td>
                </tr>
              </table>
              <p>
                Aiven tokens authenticate the workload provider only. Aiven
                modules still use the Azure state backend: the actual Terraform
                Azure identity also needs Storage Blob Data Contributor on its
                state container.
              </p>
              <p>
                Aiven modules ignore Azure subscription targeting: Aiven
                resources are addressed by Aiven project and service name.
                SEFE's Aiven BYOC infrastructure lives in the historic
                <code>SMT-NP</code> / <code>SMT-PR</code> subscriptions (<code
                  >rg-np-aiven-1-uks</code
                >
                / <code>rg-pr-aiven-1-uks</code>), which predate the
                <code>SMT-&lt;Vertical&gt;-&lt;Env&gt;</code> model and are not
                being expanded.
              </p>
            </div>
            <p>
              Values are injected onto the terraform process for that deployment
              only and secrets are redacted from logs. See
              <code>docs/Terraform/MODULES.md</code> for details.
            </p>
          </div>
        </details>
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
            ${[ALL_CATEGORIES, ...this.categories, SCRIPTS_CATEGORY].map(
              c => html`
                <vaadin-button
                  class="chip"
                  theme="small"
                  aria-pressed=${this.category === c ? 'true' : 'false'}
                  @click=${() => this.selectCategory(c)}
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
    if (this.category === SCRIPTS_CATEGORY) {
      return this.renderScriptsCatalog();
    }
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
    const groups = this.filteredGroups;
    if (!groups.length) {
      return html`
        <div class="state" role="status">
          <vaadin-icon icon="vaadin:puzzle-piece"></vaadin-icon>
          <strong
            >${this.templates.length ? 'No modules match these filters' : 'The catalog is empty'}</strong
          >
          <span
            >${this.templates.length ? 'Clear the search or pick another category.' : 'Modules are published by an administrator from the manifests directory.'}</span
          >
        </div>
      `;
    }
    return html`
      <div class="cards">
        ${groups.map(
          g => html`
            ${this.renderCard(g)}
            ${this.detailName === g.name ? this.renderDetail(g.manifest) : nothing}
          `
        )}
      </div>
    `;
  }

  private renderScriptsCatalog() {
    if (this.scriptsLoading) {
      return html`
        <div
          class="cards"
          role="status"
          aria-label="Loading PowerShell script components"
        >
          <div class="skeleton"></div>
          <div class="skeleton"></div>
        </div>
      `;
    }
    const query = this.search.trim().toLowerCase();
    const predefined = GENERIC_SCRIPT_TEMPLATES.filter(item =>
      !query
        ? true
        : [item.name, item.scriptPath, item.category, item.description].some(
            value => value.toLowerCase().includes(query)
          )
    );
    const scripts = this.filteredScripts;
    if (!predefined.length && !scripts.length) {
      return html`
        <div class="state" role="status">
          <vaadin-icon icon="vaadin:code"></vaadin-icon>
          <strong>No matching PowerShell script components</strong>
          <span
            >Clear the search or browse the global Deployment Definitions
            page.</span
          >
        </div>
      `;
    }
    const context = new URLSearchParams(window.location.search);
    const projectId = Number(context.get('projectId')) || 0;
    const projectName = context.get('project') || '';
    return html`
      <div class="cards">
        ${predefined.map(template => {
          const initialPath = serializeScriptPathValue(
            template.scriptPath,
            template.parameters.map(param => ({
              key: param.name,
              value: param.options?.[0] ?? ''
            }))
          );
          const addHref = projectId
            ? `/project-ref-data/${projectId}?addScriptName=${encodeURIComponent(template.defaultStepName)}&addScriptPath=${encodeURIComponent(initialPath)}&addPsVersion=5.1&addNonProdOnly=false&addStopOnFailure=true`
            : `/scripts`;
          return html`
            <article class="card">
              <div class="card-head">
                <div class="icon-tile tone-primary">
                  <vaadin-icon icon="vaadin:code"></vaadin-icon>
                </div>
                <div class="card-title">
                  <div class="title-row">
                    <h2>${template.name}</h2>
                    <span class="pill">00 Generic · ${template.category}</span>
                  </div>
                  <div class="meta">
                    Predefined generic script
                    (<code>${template.scriptPath}</code>)
                  </div>
                </div>
              </div>
              <p class="description">${template.description}</p>
              ${
                template.parameters.length > 0
                  ? html`<div class="meta">
                      Parameters:
                      <code
                        >${template.parameters
                          .map(p => p.name)
                          .join(', ')}</code
                      >
                    </div>`
                  : nothing
              }
              <div class="card-actions">
                <a href=${addHref}>
                  <vaadin-button theme="primary">
                    <vaadin-icon icon="vaadin:plus" slot="prefix"></vaadin-icon>
                    ${
                      projectId
                        ? `Add to ${projectName || 'project'}`
                        : 'Open in definitions'
                    }
                  </vaadin-button>
                </a>
              </div>
            </article>
          `;
        })}
        ${scripts.map(item => {
          const comp = item.Component;
          const addHref = projectId
            ? `/project-ref-data/${projectId}?addScriptName=${encodeURIComponent(comp?.ComponentName ?? 'Script step')}&addScriptPath=${encodeURIComponent(comp?.ScriptPath ?? '')}&addPsVersion=${encodeURIComponent(comp?.PSVersion ?? '5.1')}&addNonProdOnly=${comp?.NonProdOnly ? 'true' : 'false'}&addStopOnFailure=${comp?.StopOnFailure === false ? 'false' : 'true'}`
            : `/scripts`;
          return html`
            <article class="card">
              <div class="card-head">
                <div class="icon-tile tone-contrast">
                  <vaadin-icon icon="vaadin:code"></vaadin-icon>
                </div>
                <div class="card-title">
                  <div class="title-row">
                    <h2>${comp?.ComponentName || 'PowerShell Script'}</h2>
                    <span class="pill">PS ${comp?.PSVersion || '5.1'}</span>
                  </div>
                  <div class="meta">
                    PowerShell
                    Script${item.ProjectName ? ` · ${item.ProjectName}` : ''}
                  </div>
                </div>
              </div>
              <p class="description">
                <code>${item.SourceSummary || comp?.ScriptPath || '—'}</code>
              </p>
              <div class="card-actions">
                <a href=${addHref}>
                  <vaadin-button theme="primary">
                    <vaadin-icon icon="vaadin:plus" slot="prefix"></vaadin-icon>
                    ${
                      projectId
                        ? `Add to ${projectName || 'project'}`
                        : 'Open in definitions'
                    }
                  </vaadin-button>
                </a>
              </div>
            </article>
          `;
        })}
      </div>
    `;
  }

  private renderCard(g: ModuleGroup) {
    const t = g.manifest;
    const visual = moduleVisual(t);
    const params = t.Parameters ?? [];
    const required = params.filter(p => p.Required).length;
    const secrets = params.filter(p => p.Sensitive).length;
    const selected = this.detailName === g.name;
    return html`
      <article class="card" data-selected=${selected ? 'true' : 'false'}>
        <div class="card-head">
          <div class="icon-tile tone-${visual.tone}">${visual.body}</div>
          <div class="card-title">
            <div class="title-row">
              <h2>${t.Name}</h2>
              ${
                g.versions.length > 1
                  ? html`<select
                      class="pill version-select"
                      aria-label="${g.name} version"
                      .value=${t.Version}
                      @change=${(e: Event) =>
                        this.pickVersion(
                          g.name,
                          (e.target as HTMLSelectElement).value
                        )}
                    >
                      ${g.versions.map(
                        v => html`
                          <option
                            value=${v.Version}
                            ?selected=${v.Version === t.Version}
                          >
                            v${v.Version}${v.Deprecated ? ' (deprecated)' : ''}
                          </option>
                        `
                      )}
                    </select>`
                  : html`<span class="pill">v${t.Version}</span>`
              }
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
            @click=${() => this.toggleDetail(g.name)}
          >
            View inputs &amp; outputs
            <vaadin-icon
              icon=${selected ? 'vaadin:angle-up' : 'vaadin:angle-down'}
              slot="suffix"
            ></vaadin-icon>
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
    const visual = moduleVisual(t);
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
          <div class="icon-tile small tone-${visual.tone}">${visual.body}</div>
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
              @click=${() => (this.detailName = null)}
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

  /** Switches the card (and any open detail panel) to another version. */
  private pickVersion(name: string, version: string) {
    this.selectedVersions = { ...this.selectedVersions, [name]: version };
  }

  private async toggleDetail(name: string) {
    if (this.detailName === name) {
      this.detailName = null;
      return;
    }
    this.detailName = name;
    await this.updateComplete;
    this.detailPanel?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  private openDeploy(t: TerraformTemplateManifest) {
    const context = new URLSearchParams(window.location.search);
    this.deployDialog.open(t, {
      projectName: context.get('project') ?? undefined,
      environmentName: context.get('environment') ?? undefined,
      componentId: Number(context.get('componentId')) || undefined
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
