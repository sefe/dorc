import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import '@vaadin/tooltip';
import { Notification } from '@vaadin/notification';
import { css, html, nothing, svg } from 'lit';
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

/**
 * Brand icons (CC0 paths from the simple-icons project, 24x24 viewBox) so
 * modules show the toolset they actually provision instead of a generic
 * category glyph. Rendered inline with fill: currentColor so they follow the
 * icon-tile tones.
 */
const BRAND_ICON_PATHS: Record<string, { path: string; tone: string }> = {
  kafka: {
    tone: 'contrast',
    path: 'M9.71 2.136a1.43 1.43 0 0 0-2.047 0h-.007a1.48 1.48 0 0 0-.421 1.042c0 .41.161.777.422 1.039l.007.007c.257.264.616.426 1.019.426.404 0 .766-.162 1.027-.426l.003-.007c.261-.262.421-.629.421-1.039 0-.408-.159-.777-.421-1.042H9.71zM8.683 22.295c.404 0 .766-.167 1.027-.429l.003-.008c.261-.261.421-.631.421-1.036 0-.41-.159-.778-.421-1.044H9.71a1.42 1.42 0 0 0-1.027-.432 1.4 1.4 0 0 0-1.02.432h-.007c-.26.266-.422.634-.422 1.044 0 .406.161.775.422 1.036l.007.008c.258.262.617.429 1.02.429zm7.89-4.462c.359-.096.683-.33.882-.684l.027-.052a1.47 1.47 0 0 0 .114-1.067 1.454 1.454 0 0 0-.675-.896l-.021-.014a1.425 1.425 0 0 0-1.078-.132c-.36.091-.684.335-.881.686-.2.349-.241.75-.146 1.119.099.363.33.691.675.896h.002c.346.203.737.239 1.101.144zm-6.405-7.342a2.083 2.083 0 0 0-1.485-.627c-.58 0-1.103.242-1.482.627-.378.385-.612.916-.612 1.507s.233 1.124.612 1.514a2.08 2.08 0 0 0 2.967 0c.379-.39.612-.923.612-1.514s-.233-1.122-.612-1.507zm-.835-2.51c.843.141 1.6.552 2.178 1.144h.004c.092.093.182.196.265.299l1.446-.851a3.176 3.176 0 0 1-.047-1.808 3.149 3.149 0 0 1 1.456-1.926l.025-.016a3.062 3.062 0 0 1 2.345-.306c.77.21 1.465.721 1.898 1.482v.002c.431.757.518 1.626.313 2.408a3.145 3.145 0 0 1-1.456 1.928l-.198.118h-.02a3.095 3.095 0 0 1-2.154.201 3.127 3.127 0 0 1-1.514-.944l-1.444.848a4.162 4.162 0 0 1 0 2.879l1.444.846c.413-.47.939-.789 1.514-.944a3.041 3.041 0 0 1 2.371.319l.048.023v.002a3.17 3.17 0 0 1 1.408 1.906 3.215 3.215 0 0 1-.313 2.405l-.026.053-.003-.005a3.147 3.147 0 0 1-1.867 1.436 3.096 3.096 0 0 1-2.371-.318v-.006a3.156 3.156 0 0 1-1.456-1.927 3.175 3.175 0 0 1 .047-1.805l-1.446-.848a3.905 3.905 0 0 1-.265.294l-.004.005a3.938 3.938 0 0 1-2.178 1.138v1.699a3.09 3.09 0 0 1 1.56.862l.002.004c.565.572.914 1.368.914 2.243 0 .873-.35 1.664-.914 2.239l-.002.009a3.1 3.1 0 0 1-2.21.931 3.1 3.1 0 0 1-2.206-.93h-.002v-.009a3.186 3.186 0 0 1-.916-2.239c0-.875.35-1.672.916-2.243v-.004h.002a3.1 3.1 0 0 1 1.558-.862v-1.699a3.926 3.926 0 0 1-2.176-1.138l-.006-.005a4.098 4.098 0 0 1-1.173-2.874c0-1.122.452-2.136 1.173-2.872h.006a3.947 3.947 0 0 1 2.176-1.144V6.289a3.137 3.137 0 0 1-1.558-.864h-.002v-.004a3.192 3.192 0 0 1-.916-2.243c0-.871.35-1.669.916-2.243l.002-.002A3.084 3.084 0 0 1 8.683 0c.861 0 1.641.355 2.21.932v.002h.002c.565.574.914 1.372.914 2.243 0 .876-.35 1.667-.914 2.243l-.002.005a3.142 3.142 0 0 1-1.56.864v1.692zm8.121-1.129l-.012-.019a1.452 1.452 0 0 0-.87-.668 1.43 1.43 0 0 0-1.103.146h.002c-.347.2-.58.529-.677.896-.095.365-.054.768.146 1.119l.007.009c.2.347.519.579.874.673.357.103.755.059 1.098-.144l.019-.009a1.47 1.47 0 0 0 .657-.885 1.493 1.493 0 0 0-.141-1.118'
  },
  clickhouse: {
    tone: 'warning',
    path: 'M21.333 10H24v4h-2.667ZM16 1.335h2.667v21.33H16Zm-5.333 0h2.666v21.33h-2.666ZM0 22.665V1.335h2.667v21.33zm5.333-21.33H8v21.33H5.333Z'
  },
  postgresql: {
    tone: 'primary',
    path: 'M23.5594 14.7228a.5269.5269 0 0 0-.0563-.1191c-.139-.2632-.4768-.3418-1.0074-.2321-1.6533.3411-2.2935.1312-2.5256-.0191 1.342-2.0482 2.445-4.522 3.0411-6.8297.2714-1.0507.7982-3.5237.1222-4.7316a1.5641 1.5641 0 0 0-.1509-.235C21.6931.9086 19.8007.0248 17.5099.0005c-1.4947-.0158-2.7705.3461-3.1161.4794a9.449 9.449 0 0 0-.5159-.0816 8.044 8.044 0 0 0-1.3114-.1278c-1.1822-.0184-2.2038.2642-3.0498.8406-.8573-.3211-4.7888-1.645-7.2219.0788C.9359 2.1526.3086 3.8733.4302 6.3043c.0409.818.5069 3.334 1.2423 5.7436.4598 1.5065.9387 2.7019 1.4334 3.582.553.9942 1.1259 1.5933 1.7143 1.7895.4474.1491 1.1327.1441 1.8581-.7279.8012-.9635 1.5903-1.8258 1.9446-2.2069.4351.2355.9064.3625 1.39.3772a.0569.0569 0 0 0 .0004.0041 11.0312 11.0312 0 0 0-.2472.3054c-.3389.4302-.4094.5197-1.5002.7443-.3102.064-1.1344.2339-1.1464.8115-.0025.1224.0329.2309.0919.3268.2269.4231.9216.6097 1.015.6331 1.3345.3335 2.5044.092 3.3714-.6787-.017 2.231.0775 4.4174.3454 5.0874.2212.5529.7618 1.9045 2.4692 1.9043.2505 0 .5263-.0291.8296-.0941 1.7819-.3821 2.5557-1.1696 2.855-2.9059.1503-.8707.4016-2.8753.5388-4.1012.0169-.0703.0357-.1207.057-.1362.0007-.0005.0697-.0471.4272.0307a.3673.3673 0 0 0 .0443.0068l.2539.0223.0149.001c.8468.0384 1.9114-.1426 2.5312-.4308.6438-.2988 1.8057-1.0323 1.5951-1.6698z'
  },
  azure: {
    tone: 'primary',
    path: 'M22.379 23.343a1.62 1.62 0 0 0 1.536-2.14v.002L17.35 1.76A1.62 1.62 0 0 0 15.816.657H8.184A1.62 1.62 0 0 0 6.65 1.76L.086 21.204a1.62 1.62 0 0 0 1.536 2.139h4.741a1.62 1.62 0 0 0 1.535-1.103l.977-2.892 4.947 3.675c.28.208.618.32.966.32m-3.084-12.531 3.624 10.739a.54.54 0 0 1-.51.713v-.001h-.03a.54.54 0 0 1-.322-.106l-9.287-6.9h4.853m6.313 7.006c.116-.326.13-.694.007-1.058L9.79 1.76a1.722 1.722 0 0 0-.007-.02h6.034a.54.54 0 0 1 .512.366l6.562 19.445a.54.54 0 0 1-.338.684'
  },
  terraform: {
    tone: 'contrast',
    path: 'M1.44 0v7.575l6.561 3.79V3.787zm21.12 4.227l-6.561 3.791v7.574l6.56-3.787zM8.72 4.23v7.575l6.561 3.787V8.018zm0 8.405v7.575L15.28 24v-7.578z'
  }
};

const brandIconSvg = (key: string) => svg`<svg
  class="brand-icon"
  viewBox="0 0 24 24"
  aria-hidden="true"
><path d=${BRAND_ICON_PATHS[key].path}></path></svg>`;

/**
 * Picks the toolset's actual brand mark from the module's tags and required
 * providers, falling back to the category glyph when no brand matches.
 */
const moduleVisual = (t: TerraformTemplateManifest) => {
  const tags = (t.Tags ?? []).map(tag => tag.toLowerCase());
  const providers = Object.keys(t.RequiredProviders ?? {}).map(p =>
    p.toLowerCase()
  );
  let brand: string | undefined;
  if (tags.includes('kafka')) brand = 'kafka';
  else if (tags.includes('clickhouse')) brand = 'clickhouse';
  else if (tags.includes('postgresql') || tags.includes('postgres'))
    brand = 'postgresql';
  else if (tags.includes('azure') || providers.includes('azurerm'))
    brand = 'azure';
  else if (providers.length) brand = 'terraform';
  if (brand) {
    return { tone: BRAND_ICON_PATHS[brand].tone, body: brandIconSvg(brand) };
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
      .icon-tile .brand-icon {
        width: 26px;
        height: 26px;
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
        <details class="setup">
          <summary>
            <vaadin-icon icon="vaadin:key"></vaadin-icon>
            Before you deploy: set the target environment's cloud credentials
            <span class="hint">Environment page &rsaquo; Cloud tab &amp; Properties</span>
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
                type <code>Subscription</code> with the subscription GUID as
                the resource identifier. The legacy
                <code>TerraformSubscriptionId</code> property is used only when
                no subscription cloud resource is attached.
              </p>
              <p>
                Then set all three service-principal properties, or none to
                fall back to the runner host's ambient identity. The service
                principal needs RBAC on the target subscription only.
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
                Aiven modules ignore Azure subscription targeting: Aiven
                resources are addressed by Aiven project and service name.
                SEFE's Aiven BYOC infrastructure lives in the historic
                <code>SMT-NP</code> / <code>SMT-PR</code> subscriptions
                (<code>rg-np-aiven-1-uks</code> /
                <code>rg-pr-aiven-1-uks</code>), which predate the
                <code>SMT-&lt;Vertical&gt;-&lt;Env&gt;</code> model and are
                not being expanded.
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
    const visual = moduleVisual(t);
    const params = t.Parameters ?? [];
    const required = params.filter(p => p.Required).length;
    const secrets = params.filter(p => p.Sensitive).length;
    const selected = this.isSameTemplate(this.detail, t);
    return html`
      <article class="card" data-selected=${selected ? 'true' : 'false'}>
        <div class="card-head">
          <div class="icon-tile tone-${visual.tone}">${visual.body}</div>
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
