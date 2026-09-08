import '@vaadin/button';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { columnBodyRenderer } from '@vaadin/grid/lit';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import '@vaadin/dialog';
import { dialogRenderer, dialogFooterRenderer } from '@vaadin/dialog/lit';
import { Notification } from '@vaadin/notification';
import { css, html } from 'lit';
import { customElement, query, state } from 'lit/decorators.js';
import { TerraformApi, TerraformTemplateManifest } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import '../components/deploy-from-template-dialog';
import { DeployFromTemplateDialog } from '../components/deploy-from-template-dialog';

@customElement('page-stock-modules')
export class PageStockModules extends PageElement {
  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        height: calc(100vh - 50px);
        padding: 16px;
        box-sizing: border-box;
      }
      .header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 16px;
        gap: var(--lumo-space-m);
        flex-wrap: wrap;
      }
      .title {
        font-size: 24px;
        font-weight: 500;
      }
      .description {
        color: var(--lumo-secondary-text-color);
        margin: 4px 0 16px 0;
      }
      vaadin-grid {
        flex: 1;
        min-height: 0;
      }
      .deprecated-badge {
        display: inline-block;
        padding: 2px 6px;
        border-radius: var(--lumo-border-radius-s);
        background: var(--lumo-error-color-10pct);
        color: var(--lumo-error-text-color);
        font-size: var(--lumo-font-size-xs);
        font-weight: 500;
      }
      .active-badge {
        display: inline-block;
        padding: 2px 6px;
        border-radius: var(--lumo-border-radius-s);
        background: var(--lumo-success-color-10pct);
        color: var(--lumo-success-text-color);
        font-size: var(--lumo-font-size-xs);
        font-weight: 500;
      }
      .reference-block {
        font-family: 'Courier New', monospace;
        font-size: var(--lumo-font-size-s);
        background: var(--lumo-contrast-5pct);
        padding: 8px;
        border-radius: var(--lumo-border-radius-m);
        white-space: pre;
        overflow-x: auto;
      }
      .detail-section {
        margin-top: 12px;
      }
      .detail-section h4 {
        margin: 0 0 4px 0;
        color: var(--lumo-secondary-text-color);
        text-transform: uppercase;
        font-size: var(--lumo-font-size-xs);
      }
      table.params {
        width: 100%;
        border-collapse: collapse;
        font-size: var(--lumo-font-size-s);
      }
      table.params th,
      table.params td {
        text-align: left;
        padding: 4px 8px;
        border-bottom: 1px solid var(--lumo-contrast-10pct);
      }
      .filters {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--lumo-space-m);
        margin-bottom: var(--lumo-space-m);
      }
      .filters vaadin-text-field {
        flex: 1;
        min-width: 180px;
      }
    `;
  }

  @state()
  private templates: TerraformTemplateManifest[] = [];

  @state()
  private loading = false;

  @state()
  private error: string | null = null;

  @state() private search = '';
  @state() private showDeprecated = false;

  @state()
  private detailOpen = false;

  @state()
  private detail: TerraformTemplateManifest | null = null;

  @query('deploy-from-template-dialog')
  private deployDialog!: DeployFromTemplateDialog;

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

  private get filteredTemplates() {
    const query = this.search.trim().toLowerCase();
    return this.templates.filter(
      t =>
        (this.showDeprecated || !t.Deprecated) &&
        [t.Name, t.Version, t.Category, t.Description, ...(t.Tags ?? [])].some(
          value => value?.toLowerCase().includes(query)
        )
    );
  }

  render() {
    return html`
      <div class="header">
        <div>
          <h1 class="title">Terraform module catalog</h1>
          <div class="description">
            Choose a versioned module, reuse or create a project component, and
            plan changes in a mapped DOrc environment. Review and approve the
            resulting plan before applying infrastructure changes.
          </div>
        </div>
        <vaadin-button
          @click="${() => this.loadTemplates()}"
          .disabled="${this.loading}"
        >
          <vaadin-icon icon="vaadin:refresh" slot="prefix"></vaadin-icon>
          Refresh
        </vaadin-button>
      </div>

      <div class="filters">
        <vaadin-text-field
          label="Find modules"
          placeholder="Name, version, category or tag"
          .value=${this.search}
          @value-changed=${(e: CustomEvent<{ value: string }>) => (this.search = e.detail.value ?? '')}
          clear-button-visible
        ></vaadin-text-field>
        <vaadin-checkbox
          label="Include deprecated versions"
          .checked=${this.showDeprecated}
          @checked-changed=${(e: CustomEvent<{ value: boolean }>) => (this.showDeprecated = e.detail.value)}
        ></vaadin-checkbox>
      </div>
      ${this.error ? html`<div role="alert" style="color:var(--lumo-error-text-color);margin-bottom:8px;">${this.error}</div>` : ''}
      ${this.loading ? html`<p role="status">Loading Terraform modules...</p>` : ''}
      ${
        !this.loading && !this.error && !this.filteredTemplates.length
          ? html`<p role="status">
              ${this.templates.length ? 'No modules match these filters.' : 'No modules are available. The catalog must be configured by an administrator.'}
            </p>`
          : ''
      }

      <vaadin-grid
        .items="${this.filteredTemplates}"
        .loading=${this.loading}
        aria-label="Terraform module catalog"
        theme="compact row-stripes no-row-borders no-border"
      >
        <vaadin-grid-sort-column
          path="Name"
          header="Name"
        ></vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          path="Version"
          header="Version"
        ></vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          path="Category"
          header="Category"
        ></vaadin-grid-sort-column>
        <vaadin-grid-column
          header="Status"
          ${columnBodyRenderer(this.statusRenderer, [])}
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="Description"
          path="Description"
        ></vaadin-grid-column>
        <vaadin-grid-sort-column
          path="Owner"
          header="Owner"
        ></vaadin-grid-sort-column>
        <vaadin-grid-column
          header=""
          auto-width
          flex-grow="0"
          ${columnBodyRenderer(this.actionsRenderer, [])}
        ></vaadin-grid-column>
      </vaadin-grid>

      <vaadin-dialog
        .opened="${this.detailOpen}"
        @opened-changed="${(e: CustomEvent) => (this.detailOpen = e.detail.value)}"
        header-title="${this.detail?.Name ?? ''} ${this.detail?.Version ?? ''}"
        theme="wide"
        resizable
        draggable
        ${dialogRenderer(this.detailRenderer, [this.detail])}
        ${dialogFooterRenderer(this.detailFooterRenderer, [])}
      ></vaadin-dialog>

      <deploy-from-template-dialog></deploy-from-template-dialog>
    `;
  }

  private statusRenderer = (template: TerraformTemplateManifest) =>
    template.Deprecated
      ? html`<span class="deprecated-badge">Deprecated</span>`
      : html`<span class="active-badge">Active</span>`;

  // Lay the actions out in a single non-wrapping row; without this the
  // buttons overflow the auto-width cell and get clipped to an ellipsis.
  private actionsRenderer = (template: TerraformTemplateManifest) => html`
    <div style="display:flex; gap:4px; white-space:nowrap; align-items:center;">
      <vaadin-button
        theme="tertiary small"
        @click="${() => {
          this.detail = template;
          this.detailOpen = true;
        }}"
        >Details</vaadin-button
      >
      <vaadin-button
        theme="primary small"
        @click="${() => this.openDeploy(template)}"
        >Plan deployment</vaadin-button
      >
    </div>
  `;

  private openDeploy(t: TerraformTemplateManifest) {
    const context = new URLSearchParams(window.location.search);
    this.detailOpen = false;
    this.deployDialog.open(t, {
      projectName: context.get('project') ?? undefined,
      environmentName: context.get('environment') ?? undefined
    });
  }

  private detailFooterRenderer = () => html`
    <vaadin-button
      @click=${() => this.detail && this.copyReference(this.detail)}
      >Copy reference</vaadin-button
    >
    <vaadin-button
      theme="primary"
      @click=${() => this.detail && this.openDeploy(this.detail)}
      >Plan deployment</vaadin-button
    >
    <vaadin-button @click="${() => (this.detailOpen = false)}"
      >Close</vaadin-button
    >
  `;

  // Vaadin dialogRenderer always passes the Dialog instance as the renderer
  // argument; deps array is for re-render reactivity, not value-passing.
  // Read this.detail directly.
  private detailRenderer = () => {
    const template = this.detail;
    if (!template) return html``;
    return html`
      <div style="padding: 8px 16px 16px 16px;">
        <div class="description">${template.Description}</div>

        <div class="detail-section">
          <h4>Source</h4>
          <div>
            ${template.Source.Kind} :: ${template.Source.Locator} @
            <code>${template.Source.Ref}</code>
          </div>
        </div>

        <div class="detail-section">
          <h4>How to consume in a DOrc component</h4>
          <div class="reference-block">
            TerraformSourceType = Catalog TerraformTemplateName =
            ${template.Name} TerraformTemplateVersion = ${template.Version}
          </div>
          <div style="margin-top:8px;">
            Or, in your own Terraform, reference the module directly:
          </div>
          <div class="reference-block">
            module "${template.Name}" { source =
            "git::${template.Source.Locator}//${template.Source.SubPath || `stock-modules/${template.Name}`}?ref=${template.Source.Ref}"
            # ... module inputs ... }
          </div>
        </div>

        <div class="detail-section">
          <h4>Required Terraform / providers</h4>
          <div>terraform ${template.RequiredTerraformVersion}</div>
          ${Object.entries(template.RequiredProviders ?? {}).map(
            ([k, v]) => html`<div>${k} ${v}</div>`
          )}
        </div>

        <div class="detail-section">
          <h4>Inputs</h4>
          <table class="params">
            <thead>
              <tr>
                <th>Name</th>
                <th>Type</th>
                <th>Required</th>
                <th>Default</th>
                <th>Description</th>
              </tr>
            </thead>
            <tbody>
              ${(template.Parameters ?? []).map(
                p => html`
                  <tr>
                    <td><code>${p.Name}</code></td>
                    <td>${p.Type}</td>
                    <td>${p.Required ? 'yes' : 'no'}</td>
                    <td>${p.Sensitive ? '(hidden)' : (p.Default ?? '')}</td>
                    <td>${p.Description ?? ''}</td>
                  </tr>
                `
              )}
            </tbody>
          </table>
        </div>

        <div class="detail-section">
          <h4>Outputs</h4>
          <table class="params">
            <thead>
              <tr>
                <th>Name</th>
                <th>Type</th>
                <th>Sensitive</th>
                <th>Description</th>
              </tr>
            </thead>
            <tbody>
              ${(template.Outputs ?? []).map(
                o => html`
                  <tr>
                    <td><code>${o.Name}</code></td>
                    <td>${o.Type}</td>
                    <td>${o.Sensitive ? 'yes' : 'no'}</td>
                    <td>${o.Description ?? ''}</td>
                  </tr>
                `
              )}
            </tbody>
          </table>
        </div>

        <div class="detail-section">
          <h4>Tags / Owner</h4>
          <div>
            ${(template.Tags ?? []).join(', ')} &mdash;
            ${template.Owner ?? '(unknown)'}
          </div>
        </div>
      </div>
    `;
  };

  private copyReference(t: TerraformTemplateManifest) {
    const ref = `TerraformSourceType=Catalog\nTerraformTemplateName=${t.Name}\nTerraformTemplateVersion=${t.Version}`;
    navigator.clipboard.writeText(ref).then(
      () => {
        const n = Notification.show(
          `Copied catalog reference for ${t.Name}@${t.Version}`,
          {
            duration: 2000,
            position: 'bottom-end'
          }
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
