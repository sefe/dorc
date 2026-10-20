import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/grid';
import '@vaadin/text-field';
import { columnBodyRenderer } from '@vaadin/grid/lit';
import { css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { live } from 'lit/directives/live.js';
import {
  ComponentType,
  DeploymentDefinitionsApi,
  RefDataProjectsApi,
  TerraformSourceType,
  type DeploymentDefinitionApiModel,
  type ProjectApiModel
} from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

@customElement('page-deployment-definitions')
export class PageDeploymentDefinitions extends PageElement {
  @state() private items: DeploymentDefinitionApiModel[] = [];
  @state() private projects: ProjectApiModel[] = [];
  @state() private loading = false;
  @state() private error = '';
  @state() private search = '';
  @state() private projectId = '';
  @state() private componentType = '';
  @state() private sourceType = '';
  @state() private page = 1;
  @state() private total = 0;
  private readonly pageSize = 50;
  private loadToken = 0;
  private appliedFilters: {
    projectId?: number;
    componentType?: ComponentType;
    sourceType?: TerraformSourceType;
    search?: string;
  } = {};

  static styles = css`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      min-height: 0;
      color: var(--dorc-text-primary);
    }
    header,
    footer {
      padding: var(--lumo-space-m);
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-m);
      align-items: center;
      justify-content: space-between;
    }
    h1 {
      margin: 0;
      font-size: var(--lumo-font-size-xl);
    }
    p {
      color: var(--dorc-text-secondary-strong);
      max-width: 75ch;
    }
    a {
      color: var(--dorc-link-color);
    }
    form {
      padding: 0 var(--lumo-space-m) var(--lumo-space-m);
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-m);
      align-items: end;
    }
    vaadin-text-field {
      flex: 1;
      min-width: 180px;
    }
    vaadin-combo-box {
      min-width: 160px;
    }
    vaadin-grid {
      flex: 1;
      min-height: 200px;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-m);
    }
    .source {
      color: var(--dorc-text-secondary-strong);
      overflow-wrap: anywhere;
      white-space: normal;
    }
    .error {
      color: var(--lumo-error-text-color);
      margin: var(--lumo-space-m);
    }
    .empty {
      margin: var(--lumo-space-m);
    }
  `;

  protected firstUpdated() {
    this.projectId =
      new URLSearchParams(window.location.search).get('projectId') ?? '';
    this.appliedFilters = {
      projectId: this.projectId ? Number(this.projectId) : undefined
    };
    this.load();
    new RefDataProjectsApi(dorcApiConfiguration)
      .refDataProjectsGet()
      .subscribe({
        next: data => {
          this.projects = data;
        },
        error: error => {
          this.error =
            retrieveErrorMessage(error) || 'Could not load project filters.';
        }
      });
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    this.loadToken++;
  }

  private load() {
    const token = ++this.loadToken;
    this.loading = true;
    this.error = '';
    this.items = [];
    new DeploymentDefinitionsApi(dorcApiConfiguration)
      .deploymentDefinitionsGet({
        ...this.appliedFilters,
        page: this.page,
        limit: this.pageSize
      })
      .subscribe({
        next: data => {
          if (token !== this.loadToken) return;
          this.items = data.Items ?? [];
          this.total = data.TotalItems ?? 0;
          this.loading = false;
        },
        error: error => {
          if (token !== this.loadToken) return;
          this.total = 0;
          this.error =
            retrieveErrorMessage(error) ||
            'Could not load deployment definitions.';
          this.loading = false;
        }
      });
  }

  private applyFilters(event: Event) {
    event.preventDefault();
    this.appliedFilters = {
      projectId: this.projectId ? Number(this.projectId) : undefined,
      componentType: Object.values(ComponentType).find(
        type => type === this.componentType
      ),
      sourceType: Object.values(TerraformSourceType).find(
        type => type === this.sourceType
      ),
      search: this.search || undefined
    };
    this.page = 1;
    this.load();
  }

  private changePage(delta: number) {
    this.page += delta;
    this.load();
  }

  private definitionRenderer(item: DeploymentDefinitionApiModel) {
    return html`<a
        href="/project-ref-data/${item.ProjectId}?componentId=${item.Component?.ComponentId}"
        >${item.Component?.ComponentName}</a
      >
      <div class="source">
        ${item.Component?.ComponentType ?? ComponentType.PowerShell}${item.Component?.ComponentType === ComponentType.Terraform ? ` / ${item.Component.TerraformSourceType ?? TerraformSourceType.SharedFolder}` : ''}
      </div>`;
  }

  private sourceRenderer(item: DeploymentDefinitionApiModel) {
    return html`<div class="source">
        ${item.SourceSummary || 'Source not configured'}
      </div>
      <div class="source">
        Current definition: ${item.Revision || 'Not versioned'}
      </div>`;
  }

  private actionsRenderer(item: DeploymentDefinitionApiModel) {
    return html`<div class="actions">
      <a
        href="/project-ref-data/${item.ProjectId}?componentId=${item.Component?.ComponentId}"
        >${item.CanEdit ? 'Edit' : 'View'}</a
      >
      <a
        href="/project-ref-data/${item.ProjectId}?componentId=${item.Component?.ComponentId}&amp;tab=history"
        >History</a
      >
      ${item.CanDeploy ? html`<a href="/deploy?projectId=${item.ProjectId}&amp;componentId=${item.Component?.ComponentId}">Deploy</a>` : nothing}
    </div>`;
  }

  render() {
    return html`
      <header>
        <div>
          <h1>Deployment definitions</h1>
          <p>
            Current PowerShell and Terraform components. A shared script is
            shown through its consuming components; deployment evidence remains
            in History.
          </p>
        </div>
        <div class="actions">
          <a href="/module-catalog">Module catalog</a
          ><a href="/scripts">Legacy scripts, including orphaned scripts</a>
        </div>
      </header>
      <form @submit=${(event: Event) => this.applyFilters(event)}>
        <vaadin-text-field
          label="Search definitions"
          .value=${this.search}
          clear-button-visible
          @value-changed=${(event: CustomEvent<{ value: string }>) => {
            this.search = event.detail.value;
          }}
        ></vaadin-text-field>
        <vaadin-combo-box
          label="Project"
          .items=${this.projects}
          item-label-path="ProjectName"
          item-value-path="ProjectId"
          .value=${live(this.projectId)}
          clear-button-visible
          @change=${(event: Event) => {
            this.projectId = (event.currentTarget as HTMLInputElement).value;
          }}
        ></vaadin-combo-box>
        <vaadin-combo-box
          label="Execution type"
          .items=${Object.values(ComponentType)}
          .value=${this.componentType}
          clear-button-visible
          @value-changed=${(event: CustomEvent<{ value: string }>) => {
            this.componentType = event.detail.value;
            if (this.componentType === ComponentType.PowerShell)
              this.sourceType = '';
          }}
        ></vaadin-combo-box>
        <vaadin-combo-box
          label="Terraform source"
          .items=${Object.values(TerraformSourceType)}
          .value=${this.sourceType}
          .disabled=${this.componentType === ComponentType.PowerShell}
          clear-button-visible
          @value-changed=${(event: CustomEvent<{ value: string }>) => {
            this.sourceType = event.detail.value;
          }}
        ></vaadin-combo-box>
        <vaadin-button
          theme="primary"
          @click=${(event: Event) => this.applyFilters(event)}
          .disabled=${this.loading}
          >Apply filters</vaadin-button
        >
      </form>
      ${this.error ? html`<p class="error" role="alert">${this.error}</p>` : nothing}
      ${this.loading ? html`<p class="empty" role="status">Loading definitions...</p>` : nothing}
      ${!this.loading && !this.error && !this.items.length ? html`<p class="empty">No definitions match these filters.</p>` : nothing}
      <vaadin-grid
        .items=${this.items}
        .loading=${this.loading}
        theme="compact row-stripes no-border"
      >
        <vaadin-grid-column
          header="Definition / type"
          ${columnBodyRenderer(this.definitionRenderer, [])}
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="ProjectName"
          header="Project"
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="Source / revision"
          ${columnBodyRenderer(this.sourceRenderer, [])}
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="Actions"
          ${columnBodyRenderer(this.actionsRenderer, [])}
        ></vaadin-grid-column>
      </vaadin-grid>
      <footer>
        <span
          >${this.total}
          definitions${this.total ? `; page ${this.page} of ${Math.ceil(this.total / this.pageSize)}` : ''}</span
        >
        <div>
          <vaadin-button
            @click=${() => this.changePage(-1)}
            .disabled=${this.loading || this.page <= 1}
            >Previous</vaadin-button
          >
          <vaadin-button
            @click=${() => this.changePage(1)}
            .disabled=${this.loading || this.page * this.pageSize >= this.total}
            >Next</vaadin-button
          >
        </div>
      </footer>
    `;
  }
}
