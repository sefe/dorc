import '@vaadin/button';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import '@vaadin/dialog';
import { dialogRenderer } from '@vaadin/dialog/lit';
import { Notification } from '@vaadin/notification';
import { css, html, LitElement } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import {
  TerraformApi,
  TerraformTemplateManifest,
} from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';

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
      }
      .title {
        font-size: 24px;
        font-weight: 500;
      }
      .description {
        color: var(--lumo-secondary-text-color);
        margin: 4px 0 16px 0;
      }
      .grid-container {
        flex: 1;
        overflow: auto;
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
      table.params th, table.params td {
        text-align: left;
        padding: 4px 8px;
        border-bottom: 1px solid var(--lumo-contrast-10pct);
      }
    `;
  }

  @state()
  private templates: TerraformTemplateManifest[] = [];

  @state()
  private loading = false;

  @state()
  private error: string | null = null;

  @state()
  private detailOpen = false;

  @state()
  private detail: TerraformTemplateManifest | null = null;

  private terraformApi = new TerraformApi();

  connectedCallback() {
    super.connectedCallback();
    this.loadTemplates();
  }

  private loadTemplates() {
    this.loading = true;
    this.error = null;
    this.terraformApi.terraformTemplatesGet().subscribe({
      next: (data) => {
        this.templates = data ?? [];
        this.loading = false;
      },
      error: (err) => {
        this.error = retrieveErrorMessage(err) ?? 'Failed to load stock modules.';
        this.loading = false;
      },
    });
  }

  render() {
    return html`
      <div class="header">
        <div>
          <div class="title">Stock Terraform modules</div>
          <div class="description">
            Curated Terraform modules engineers can use as starting points. Reference one
            from a Terraform component by setting the catalog name + version, or by
            cloning the source repo at the pinned tag shown in the details panel.
          </div>
        </div>
        <vaadin-button @click="${() => this.loadTemplates()}" .disabled="${this.loading}">
          <vaadin-icon icon="vaadin:refresh" slot="prefix"></vaadin-icon>
          Refresh
        </vaadin-button>
      </div>

      ${this.error ? html`<div style="color:var(--lumo-error-text-color);margin-bottom:8px;">${this.error}</div>` : ''}

      <div class="grid-container">
        <vaadin-grid .items="${this.templates}" all-rows-visible>
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
          <vaadin-grid-column header="Status" .renderer="${this.statusRenderer}"></vaadin-grid-column>
          <vaadin-grid-column header="Description" path="Description"></vaadin-grid-column>
          <vaadin-grid-sort-column
            path="Owner"
            header="Owner"
          ></vaadin-grid-sort-column>
          <vaadin-grid-column header="" .renderer="${this.actionsRenderer.bind(this)}"></vaadin-grid-column>
        </vaadin-grid>
      </div>

      <vaadin-dialog
        .opened="${this.detailOpen}"
        @opened-changed="${(e: CustomEvent) => (this.detailOpen = e.detail.value)}"
        header-title="${this.detail?.Name ?? ''} ${this.detail?.Version ?? ''}"
        resizable
        draggable
        modeless
        ${dialogRenderer(this.detailRenderer.bind(this), [this.detail])}
      ></vaadin-dialog>
    `;
  }

  private statusRenderer(root: HTMLElement, _: any, model: { item: TerraformTemplateManifest }) {
    const t = model.item;
    root.innerHTML = t.Deprecated
      ? `<span class="deprecated-badge">Deprecated</span>`
      : `<span class="active-badge">Active</span>`;
  }

  private actionsRenderer(root: HTMLElement, _: any, model: { item: TerraformTemplateManifest }) {
    root.innerHTML = '';
    const btn = document.createElement('vaadin-button');
    btn.setAttribute('theme', 'tertiary small');
    btn.textContent = 'Details';
    btn.addEventListener('click', () => {
      this.detail = model.item;
      this.detailOpen = true;
    });
    root.appendChild(btn);

    const ref = document.createElement('vaadin-button');
    ref.setAttribute('theme', 'tertiary small');
    ref.textContent = 'Copy reference';
    ref.addEventListener('click', () => this.copyReference(model.item));
    root.appendChild(ref);
  }

  private detailRenderer = (template: TerraformTemplateManifest | null) => {
    if (!template) return html``;
    return html`
      <div style="padding: 8px 16px 16px 16px; max-width: 720px;">
        <div class="description">${template.Description}</div>

        <div class="detail-section">
          <h4>Source</h4>
          <div>${template.Source.Kind} :: ${template.Source.Locator} @ <code>${template.Source.Ref}</code></div>
        </div>

        <div class="detail-section">
          <h4>How to consume in a DOrc component</h4>
          <div class="reference-block">TerraformSourceType    = Catalog
TerraformTemplateName  = ${template.Name}
TerraformTemplateVersion = ${template.Version}</div>
          <div style="margin-top:8px;">Or, in your own Terraform, reference the module directly:</div>
          <div class="reference-block">module "${template.Name}" {
  source = "git::${template.Source.Locator}//stock-modules/${template.Name}?ref=${template.Source.Ref}"
  # ... module inputs ...
}</div>
        </div>

        <div class="detail-section">
          <h4>Required Terraform / providers</h4>
          <div>terraform ${template.RequiredTerraformVersion}</div>
          ${Object.entries(template.RequiredProviders ?? {}).map(
            ([k, v]) => html`<div>${k} ${v}</div>`,
          )}
        </div>

        <div class="detail-section">
          <h4>Inputs</h4>
          <table class="params">
            <thead>
              <tr><th>Name</th><th>Type</th><th>Required</th><th>Default</th><th>Description</th></tr>
            </thead>
            <tbody>
              ${(template.Parameters ?? []).map(
                (p) => html`
                  <tr>
                    <td><code>${p.Name}</code></td>
                    <td>${p.Type}</td>
                    <td>${p.Required ? 'yes' : 'no'}</td>
                    <td>${p.Default ?? ''}</td>
                    <td>${p.Description ?? ''}</td>
                  </tr>
                `,
              )}
            </tbody>
          </table>
        </div>

        <div class="detail-section">
          <h4>Outputs</h4>
          <table class="params">
            <thead><tr><th>Name</th><th>Type</th><th>Sensitive</th><th>Description</th></tr></thead>
            <tbody>
              ${(template.Outputs ?? []).map(
                (o) => html`
                  <tr>
                    <td><code>${o.Name}</code></td>
                    <td>${o.Type}</td>
                    <td>${o.Sensitive ? 'yes' : 'no'}</td>
                    <td>${o.Description ?? ''}</td>
                  </tr>
                `,
              )}
            </tbody>
          </table>
        </div>

        <div class="detail-section">
          <h4>Tags / Owner</h4>
          <div>${(template.Tags ?? []).join(', ')} &mdash; ${template.Owner ?? '(unknown)'}</div>
        </div>
      </div>
    `;
  };

  private copyReference(t: TerraformTemplateManifest) {
    const ref = `TerraformSourceType=Catalog\nTerraformTemplateName=${t.Name}\nTerraformTemplateVersion=${t.Version}`;
    navigator.clipboard.writeText(ref).then(
      () => {
        const n = Notification.show(`Copied catalog reference for ${t.Name}@${t.Version}`, {
          duration: 2000,
          position: 'bottom-end',
        });
        n.setAttribute('theme', 'success');
      },
      () => {
        Notification.show('Clipboard copy failed.', { duration: 2000, position: 'bottom-end' });
      },
    );
  }
}
