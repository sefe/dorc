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
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { drawerShortcuts } from '../components/drawer-shortcuts';

@customElement('page-deployment-definitions')
export class PageDeploymentDefinitions extends ResponsiveMixin(PageElement) {
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
  private loadGeneration = 0;
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
      padding: var(--lumo-space-xs) var(--lumo-space-m);
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-s);
      align-items: center;
      justify-content: space-between;
    }
    header {
      border-bottom: 1px solid var(--dorc-border-color);
    }
    footer {
      border-top: 1px solid var(--dorc-border-color);
      font-size: var(--lumo-font-size-s);
    }
    h1 {
      margin: 0;
      font-size: var(--lumo-font-size-l);
    }
    p {
      margin: 0;
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
      max-width: 75ch;
    }
    a {
      color: var(--dorc-link-color);
      font-size: var(--lumo-font-size-s);
    }
    form {
      padding: var(--lumo-space-xs) var(--lumo-space-m);
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-s);
      align-items: end;
      border-bottom: 1px solid var(--dorc-border-color);
    }
    vaadin-text-field {
      flex: 1;
      min-width: 180px;
    }
    vaadin-combo-box {
      min-width: 150px;
    }
    vaadin-grid {
      flex: 1;
      min-height: 0;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-s);
    }
    .source {
      color: var(--dorc-text-secondary-strong);
      overflow-wrap: anywhere;
      white-space: normal;
      font-size: var(--lumo-font-size-xs);
    }
    .error {
      color: var(--lumo-error-text-color);
      margin: var(--lumo-space-xs) var(--lumo-space-m);
    }
    .empty {
      margin: var(--lumo-space-xs) var(--lumo-space-m);
    }
    @media (max-width: 768px) {
      header p {
        display: none;
      }
      form {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        padding: var(--lumo-space-xs) var(--lumo-space-s);
      }
      form vaadin-text-field {
        grid-column: 1 / -1;
        min-width: 0;
      }
      form vaadin-combo-box {
        min-width: 0;
        width: 100%;
      }
      vaadin-grid-cell-content {
        white-space: normal;
        word-wrap: break-word;
        overflow-wrap: break-word;
      }
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
    this.loadGeneration++;
  }

  private load() {
    const generation = ++this.loadGeneration;
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
          if (generation !== this.loadGeneration) return;
          this.items = data.Items ?? [];
          this.total = data.TotalItems ?? 0;
          this.loading = false;
        },
        error: error => {
          if (generation !== this.loadGeneration) return;
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

  private pinProject(item: DeploymentDefinitionApiModel) {
    if (!item.ProjectName) return;
    drawerShortcuts.add('projects', {
      ProjectId: item.ProjectId,
      ProjectName: item.ProjectName
    });
  }

  private definitionRenderer(item: DeploymentDefinitionApiModel) {
    return html`<a
        href="/project-ref-data/${item.ProjectId}?componentId=${item.Component?.ComponentId}"
        @click=${() => this.pinProject(item)}
        >${item.Component?.ComponentName}</a
      >
      <div class="source">
        ${this._narrowScreen && item.ProjectName ? `${item.ProjectName} · ` : ''}${item.Component?.ComponentType ?? ComponentType.PowerShell}${item.Component?.ComponentType === ComponentType.Terraform ? ` / ${item.Component.TerraformSourceType ?? TerraformSourceType.SharedFolder}` : ''}
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
        @click=${() => this.pinProject(item)}
        >${item.CanEdit ? 'Edit' : 'View'}</a
      >
      <a
        href="/project-ref-data/${item.ProjectId}?componentId=${item.Component?.ComponentId}&amp;tab=history"
        @click=${() => this.pinProject(item)}
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
          theme="small"
          label="Search definitions"
          .value=${this.search}
          clear-button-visible
          @value-changed=${(event: CustomEvent<{ value: string }>) => {
            this.search = event.detail.value;
          }}
        ></vaadin-text-field>
        <vaadin-combo-box
          theme="small"
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
          theme="small"
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
          theme="small"
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
          theme="primary small"
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
        theme="compact row-stripes no-row-borders no-border"
      >
        <vaadin-grid-column
          header="Definition / type"
          ${columnBodyRenderer(this.definitionRenderer, [this._narrowScreen])}
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="ProjectName"
          header="Project"
          ?hidden=${this._narrowScreen}
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="Source / revision"
          ?hidden=${this._narrowScreen}
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
            theme="small"
            @click=${() => this.changePage(-1)}
            .disabled=${this.loading || this.page <= 1}
            >Previous</vaadin-button
          >
          <vaadin-button
            theme="small"
            @click=${() => this.changePage(1)}
            .disabled=${this.loading || this.page * this.pageSize >= this.total}
            >Next</vaadin-button
          >
        </div>
      </footer>
    `;
  }
}
