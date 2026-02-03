import { css, PropertyValues } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { GridColumn, GridItemModel } from '@vaadin/grid';
import { TextField } from '@vaadin/text-field';
import '../icons/iron-icons';
import { ErrorNotification } from '../components/notifications/error-notification';
import { PageElement } from '../helpers/page-element';
import { 
  RefDataComponentsApi,
  ComponentApiModel,
  RefDataProjectEnvironmentMappingsApi,
  EnvironmentApiModel,
  RefDataProjectBuildsApi,
  EnvironmentContentBuildsApiModel,
  RefDataProjectsApi
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';

interface ComponentDeploymentInfo extends ComponentApiModel {
  environmentBuilds?: Map<string, EnvironmentContentBuildsApiModel | null>;
  displayName?: string;
  parentPath?: string[];
}

@customElement('page-project-components')
export class PageProjectComponents extends PageElement {
    static get styles() {
        return css`
      :host {
        display: flex;
        flex-direction: column;
        height: calc(100vh - 50px);
        padding: 16px;
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

      .filter-container {
        display: flex;
        gap: 10px;
        align-items: center;
        margin-bottom: 16px;
      }

      .grid-container {
        flex: 1;
        overflow: auto;
        position: relative;
      }

      vaadin-grid {
        height: 100%;
      }

      .loader {
        position: fixed;
        border: 10px solid #f3f3f3;
        border-top: 10px solid #3498db;
        border-radius: 50%;
        width: 56px;
        height: 56px;
        animation: spin 2s linear infinite;
        z-index: 998;
        right: 30px;
        bottom: 50px;
      }

      @keyframes spin {
        0% { transform: rotate(0deg); }
        100% { transform: rotate(360deg); }
      }

      .enabled {
        color: #4caf50;
        font-weight: bold;
      }

      .disabled {
        color: #f44336;
      }

      .build-info {
        font-size: var(--lumo-font-size-s);
        color: var(--lumo-secondary-text-color);
      }

      .build-number {
        font-weight: 500;
        color: var(--lumo-body-text-color);
      }

      .status-complete {
        color: #4caf50;
      }

      .status-failed {
        color: #f44336;
      }

      .status-cancelled {
        color: #ff9800;
      }

      .no-deployment {
        color: var(--lumo-disabled-text-color);
        font-style: italic;
      }

      .component-name-container {
        display: flex;
        flex-direction: column;
        gap: 4px;
        align-items: flex-start;
      }

      .parent-badge {
        display: inline-block;
        padding: 2px 8px;
        border-radius: 4px;
        font-size: var(--lumo-font-size-xs);
        font-weight: 500;
        background-color: var(--lumo-contrast-10pct);
        color: var(--lumo-secondary-text-color);
        white-space: nowrap;
      }

      .component-name {
        font-size: var(--lumo-font-size-m);
      }

      .env-column {
        min-width: 200px;
      }

      .request-link {
        cursor: pointer;
        color: var(--lumo-primary-text-color);
        text-decoration: underline;
      }

      .request-link:hover {
        color: var(--lumo-primary-color);
      }
    `;
    }

    private projectId: number | undefined;
    private projectName: string = '';

    @property({ type: Array })
    private components: ComponentApiModel[] = [];

    @property({ type: Array })
    private filteredComponents: ComponentDeploymentInfo[] = [];

    @property({ type: Array })
    private environments: EnvironmentApiModel[] = [];

    @property({ type: Map })
    private envBuildsMap: Map<string, EnvironmentContentBuildsApiModel[]> = new Map();

    @property({ type: Boolean })
    private refDataLoading = false;

    @property({ type: Boolean })
    private environmentsLoading = false;

    @property({ type: String })
    private componentNameFilter = '';

    render() {
        return html`
      <div class="header">
        <div class="title">
          ${this.projectName
                ? `${this.projectName} - Component Deployments`
                : 'Project Component Deployments'}
        </div>
      </div>
      <div class="filter-container">
        <vaadin-text-field
          placeholder="Filter by component name..."
          @input="${this.handleComponentNameFilter}"
          clear-button-visible
          helper-text="Use | for multiple search terms"
          style="min-width: 400px;"
        >
          <vaadin-icon slot="prefix" icon="vaadin:search"></vaadin-icon>
        </vaadin-text-field>
      </div>
      <div class="grid-container">
        <vaadin-grid
          .items="${this.filteredComponents}"
          theme="compact row-stripes no-row-borders no-border"
          all-rows-visible
        >
          <vaadin-grid-column
            header="Component Name"
            frozen
            resizable
            auto-width
            flex-grow="0"
            .renderer="${this.componentNameRenderer}"
          >
          </vaadin-grid-column>
          <vaadin-grid-sort-column
            header="Enabled"
            path="IsEnabled"
            resizable
            width="100px"
            .renderer="${this.enabledRenderer}"
          >
          </vaadin-grid-sort-column>
          ${this.environments.map(
                    env => html`
              <vaadin-grid-column
                class="env-column"
                header="${env.EnvironmentName}"
                resizable
                .renderer="${(root: HTMLElement, _column: GridColumn, model: GridItemModel<ComponentDeploymentInfo>) =>
                            this.envDeploymentRenderer(root, env.EnvironmentName!, model)}"
              >
              </vaadin-grid-column>
            `
                )}
        </vaadin-grid>
      </div>
      <div class="loader" ?hidden="${!this.refDataLoading && !this.environmentsLoading}"></div>
    `;
    }

    protected firstUpdated(_changedProperties: PropertyValues) {
        super.firstUpdated(_changedProperties);

        this.projectId = parseInt(
            location.pathname.substring(location.pathname.lastIndexOf('/') + 1),
            10
        );

        this.refDataLoading = true;
        this.loadProjectData();
    }

    private async loadProjectData() {
        try {
            await this.getProjectName(this.projectId!);
            await this.getProjectComponents(this.projectName);
            await this.loadProjectEnvironments();
            await this.loadEnvironmentBuilds();
            this.applyFilter();
        } catch (error) {
            console.error('Error loading project data:', error);
            this.refDataLoading = false;
        }
    }

    private getProjectName(projId: number): Promise<void> {
        return new Promise((resolve, reject) => {
            const api = new RefDataProjectsApi();
            api.refDataProjectsGet().subscribe({
                next: projects => {
                    const project = projects.find(p => p.ProjectId === projId);
                    if (project) {
                        this.projectName = project.ProjectName || '';
                        resolve();
                    } else {
                        reject(new Error('Project not found'));
                    }
                },
                error: (err: any) => {
                    const errMsg = retrieveErrorMessage(err);
                    this.errorAlert(errMsg);
                    console.error(err);
                    reject(err);
                }
            });
        });
    }

    private getProjectComponents(projectName: string): Promise<void> {
        return new Promise((resolve, reject) => {
            const api = new RefDataComponentsApi();
            api.refDataComponentsGet({ id: projectName }).subscribe({
                next: value => {
                    this.components = value.Items || [];
                    resolve();
                },
                error: (err: any) => {
                    const errMsg = retrieveErrorMessage(err);
                    this.errorAlert(errMsg);
                    console.error(err);
                    this.refDataLoading = false;
                    reject(err);
                }
            });
        });
    }

    private loadProjectEnvironments(): Promise<void> {
        return new Promise((resolve, reject) => {
            if (!this.projectName) {
                resolve();
                return;
            }

            this.environmentsLoading = true;
            const api = new RefDataProjectEnvironmentMappingsApi();
            api
                .refDataProjectEnvironmentMappingsGet({
                    project: this.projectName,
                    includeRead: true
                })
                .subscribe({
                    next: data => {
                        this.environments = (data.Items || []).sort((a, b) =>
                            (a.EnvironmentName || '').localeCompare(b.EnvironmentName || '')
                        );
                        this.environmentsLoading = false;
                        resolve();
                    },
                    error: (err: any) => {
                        const errMsg = retrieveErrorMessage(err);
                        this.errorAlert(errMsg);
                        console.error(err);
                        this.environmentsLoading = false;
                        reject(err);
                    }
                });
        });
    }

    private async loadEnvironmentBuilds(): Promise<void> {
        const buildPromises = this.environments.map(env =>
            this.loadBuildsForEnvironment(env.EnvironmentName!)
        );

        try {
            await Promise.all(buildPromises);
            this.refDataLoading = false;
        } catch (error) {
            console.error('Error loading environment builds:', error);
            this.refDataLoading = false;
        }
    }

    private loadBuildsForEnvironment(envName: string): Promise<void> {
        return new Promise((resolve) => {
            const api = new RefDataProjectBuildsApi();
            api.refDataProjectBuildsGet({ id: envName }).subscribe({
                next: (builds: EnvironmentContentBuildsApiModel[]) => {
                    this.envBuildsMap.set(envName, builds);
                    resolve();
                },
                error: (err: any) => {
                    console.error(`Error loading builds for environment ${envName}:`, err);
                    this.envBuildsMap.set(envName, []);
                    resolve();
                }
            });
        });
    }

    private handleComponentNameFilter(e: InputEvent) {
        const textField = e.target as TextField;
        this.componentNameFilter = textField.value || '';
        this.applyFilter();
    }

    private applyFilter() {
        // Build a set of all child component IDs first to exclude them from top-level processing
        const childIds = this.collectAllChildIds(this.components);
        const flattenedAll = this.flattenComponents(this.components, [], new Set<string>(), childIds);

        if (!this.componentNameFilter) {
            this.filteredComponents = flattenedAll;
            return;
        }

        const filters = this.componentNameFilter
            .trim()
            .split('|')
            .map(filter => new RegExp(filter, 'i'));

        this.filteredComponents = flattenedAll.filter(component =>
            filters.some(filter => {
                if (filter.test(component.displayName || '')) {
                    return true;
                }
                if (component.parentPath) {
                    return component.parentPath.some(parent => filter.test(parent));
                }
                return false;
            })
        );
    }

    private collectAllChildIds(components: ComponentApiModel[]): Set<string> {
        const childIds = new Set<string>();
        
        const collectChildren = (comps: ComponentApiModel[]) => {
            comps.forEach(comp => {
                if (comp.Children && comp.Children.length > 0) {
                    comp.Children.forEach(child => {
                        childIds.add(child.ComponentId?.toString() || '');
                        collectChildren(comp.Children!);
                    });
                }
            });
        };
        
        collectChildren(components);
        return childIds;
    }

    private flattenComponents(
        components: ComponentApiModel[],
        parentPath: string[],
        visited: Set<string>,
        childIds: Set<string>
    ): ComponentDeploymentInfo[] {
        if (!components || components.length === 0) return [];

        const result: ComponentDeploymentInfo[] = [];

        const sortedComponents = [...components].sort((a, b) =>
            (a.ComponentName || '').localeCompare(b.ComponentName || '')
        );

        sortedComponents.forEach(component => {
            const componentName = component.ComponentName || '';
            const componentId = component.ComponentId?.toString() || '';
            
            if (visited.has(componentId)) {
                return;
            }
            
            if (parentPath.length === 0 && childIds.has(componentId)) {
                return;
            }
            
            visited.add(componentId);

            const hasScript = Boolean(component.ScriptPath && component.ScriptPath.trim() !== '');
            const hasChildren = Boolean(component.Children && component.Children.length > 0);

            if (hasScript) {
                const deploymentInfo: ComponentDeploymentInfo = {
                    ...component,
                    displayName: componentName,
                    parentPath: parentPath.length > 0 ? [...parentPath] : undefined,
                    environmentBuilds: new Map()
                };

                this.environments.forEach(env => {
                    const envBuilds = this.envBuildsMap.get(env.EnvironmentName!);
                    const componentBuild = envBuilds?.find(
                        build => build.ComponentName === componentName
                    );
                    deploymentInfo.environmentBuilds!.set(
                        env.EnvironmentName!,
                        componentBuild || null
                    );
                });
                
                result.push(deploymentInfo);
            }

            if (hasChildren) {
                const childParentPath = [...parentPath, componentName];
                result.push(...this.flattenComponents(component.Children!, childParentPath, visited, childIds));
            }
        });

        return result;
    }


  private componentNameRenderer(
    root: HTMLElement,
    _column: any,
    model: GridItemModel<ComponentDeploymentInfo>
  ) {
    const component = model.item;
    
    if (component.parentPath && component.parentPath.length > 0) {
      const hierarchyPath = component.parentPath.join(' / ');
      
      root.innerHTML = `
        <div class="component-name-container">
          <span class="parent-badge">${hierarchyPath}</span>
          <span class="component-name">${component.displayName}</span>
        </div>
      `;
    } else {
      root.innerHTML = `<span class="component-name">${component.displayName}</span>`;
    }
  }

  private enabledRenderer(
    root: HTMLElement,
    _column: any,
    model: GridItemModel<ComponentDeploymentInfo>
  ) {
    const enabled = model.item.IsEnabled;
    root.innerHTML = `<span class="${enabled ? 'enabled' : 'disabled'}">${
      enabled ? 'Yes' : 'No'
    }</span>`;
  }

  private envDeploymentRenderer(
    root: HTMLElement,
    envName: string,
    model: GridItemModel<ComponentDeploymentInfo>
  ) {
    const component = model.item;
    const build = component.environmentBuilds?.get(envName);

    if (!build) {
      root.innerHTML = '<span class="no-deployment">Not deployed</span>';
      return;
    }

    const statusClass = this.getStatusClass(build.State);
    const buildNumber = build.RequestBuildNum || 'N/A';
    const status = build.State || 'Unknown';
    const updateDate = build.UpdateDate 
      ? new Date(build.UpdateDate).toLocaleDateString('en-GB', {
          day: '2-digit',
          month: '2-digit',
          year: '2-digit'
        })
      : '';

    root.innerHTML = `
      <div class="build-info">
        <div class="build-number">
          ${build.RequestId ? `
            <span 
              class="request-link" 
              data-request-id="${build.RequestId}"
            >
              ${buildNumber}
            </span>
          ` : buildNumber}
        </div>
        <div class="${statusClass}">${status}</div>
        ${updateDate ? `<div>${updateDate}</div>` : ''}
      </div>
    `;

    const link = root.querySelector('.request-link');
    if (link && build.RequestId) {
      link.addEventListener('click', () => {
        window.open(`/monitor-result/${build.RequestId}`, '_blank');
      });
    }
  }

  private getStatusClass(status: string | null | undefined): string {
    if (!status) return '';
    
    const statusLower = status.toLowerCase();
    if (statusLower === 'complete') return 'status-complete';
    if (statusLower === 'failed') return 'status-failed';
    if (statusLower === 'cancelled') return 'status-cancelled';
    return '';
  }

  private errorAlert(err: string) {
    const notification = new ErrorNotification();
    notification.setAttribute('errorMessage', err);
    this.shadowRoot?.appendChild(notification);
    notification.open();
  }
}
