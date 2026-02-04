import { css, PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/combo-box';
import {  ComboBoxSelectedItemChangedEvent } from '@vaadin/combo-box';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { GridColumn } from '@vaadin/grid/src/vaadin-grid-column';
import { GridItemModel } from '@vaadin/grid';
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
  Children?: ComponentDeploymentInfo[];
  environmentBuilds?: Map<string, EnvironmentContentBuildsApiModel | null>;
  displayName?: string;
  parentPath?: string[];
}

interface EnvironmentDeploymentRow {
  environmentName: string;
  buildNumber: string;
  status: string;
  updateDate: string;
  requestId?: number;
  build?: EnvironmentContentBuildsApiModel | null;
  componentId?: string;
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
        box-sizing: border-box;
      }

      .header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 16px;
        flex-shrink: 0;
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
        flex-shrink: 0;
      }

      .grid-container {
        flex: 1;
        overflow: auto;
        position: relative;
        min-height: 0;
        padding-bottom: 20px;
      }

      vaadin-grid {
        height: 100%;
        width: 100%;
      }

      .loader {
        position: absolute;
        top: 50%;
        left: 50%;
        transform: translate(-50%, -50%);
        border: 10px solid #f3f3f3;
        border-top: 10px solid #3498db;
        border-radius: 50%;
        width: 56px;
        height: 56px;
        animation: spin 2s linear infinite;
        z-index: 998;
      }

      @keyframes spin {
        0% { transform: rotate(0deg); }
        100% { transform: rotate(360deg); }
      }

      .component-picker {
        width: 100%;
        max-width: 600px;
      }

      .build-number {
        font-weight: 500;
        color: var(--lumo-body-text-color);
      }

      .status-complete {
        color: #4caf50;
        font-weight: 500;
      }

      .status-failed {
        color: #f44336;
        font-weight: 500;
      }

      .status-cancelled {
        color: #ff9800;
        font-weight: 500;
      }

      .no-deployment {
        color: var(--lumo-disabled-text-color);
        font-style: italic;
      }

      .request-link {
        cursor: pointer;
        color: var(--lumo-primary-text-color);
        text-decoration: underline;
      }

      .request-link:hover {
        text-decoration: none;
      }

      .no-selection {
        display: flex;
        align-items: center;
        justify-content: center;
        height: 100%;
        color: var(--lumo-secondary-text-color);
        font-size: var(--lumo-font-size-l);
      }
    `;
    }

    private projectId: number | undefined;
    private projectName: string = '';

    @property({ type: Array })
    private components: ComponentApiModel[] = [];

    @property({ type: Array })
    private filteredComponents: ComponentDeploymentInfo[] = [];

    @state()
    private selectedComponent: ComponentDeploymentInfo | null = null;

    @state()
    private deploymentRows: EnvironmentDeploymentRow[] = [];

    @property({ type: Array })
    private environments: EnvironmentApiModel[] = [];

    @property({ type: Map })
    private envBuildsMap: Map<string, EnvironmentContentBuildsApiModel[]> = new Map();

    @property({ type: Boolean })
    private refDataLoading = false;

    @property({ type: Boolean })
    private environmentsLoading = false;

    private boundBuildNumberRenderer = this.buildNumberRenderer.bind(this);
    private boundStatusRenderer = this.statusRenderer.bind(this);
    private boundDateRenderer = this.dateRenderer.bind(this);

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
        <vaadin-combo-box
          class="component-picker"
          label="Select Component"
          placeholder="Choose a component..."
          .items="${this.filteredComponents}"
          .itemLabelPath="${'displayName'}"
          .filteredItems="${this.filteredComponents}"
          @selected-item-changed="${this.handleComponentSelected}"
          clear-button-visible
        >
        </vaadin-combo-box>
      </div>
      <div class="grid-container">
        ${this.selectedComponent
          ? html`
              <vaadin-grid
                .items="${this.deploymentRows}"
                theme="compact row-stripes no-row-borders"
              >
                <vaadin-grid-sort-column
                  header="Environment"
                  path="environmentName"
                  resizable
                  auto-width
                >
                </vaadin-grid-sort-column>
                <vaadin-grid-column
                  header="Build Number"
                  path="buildNumber"
                  resizable
                  auto-width
                  .renderer="${this.boundBuildNumberRenderer}"
                >
                </vaadin-grid-column>
                <vaadin-grid-sort-column
                  header="Status"
                  path="status"
                  resizable
                  auto-width
                  .renderer="${this.boundStatusRenderer}"
                >
                </vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  header="Date"
                  path="updateDate"
                  resizable
                  auto-width
                  .renderer="${this.boundDateRenderer}"
                >
                </vaadin-grid-sort-column>
              </vaadin-grid>
            `
          : !this.refDataLoading && !this.environmentsLoading
          ? html`
              <div class="no-selection">
                Select a component from the dropdown to view deployment information
              </div>
            `
          : ''}
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

    private handleComponentSelected(e: ComboBoxSelectedItemChangedEvent<ComponentDeploymentInfo>) {
        this.selectedComponent = e.detail.value ?? null;
        this.updateDeploymentRows();
    }

    private updateDeploymentRows() {
        if (!this.selectedComponent) {
            this.deploymentRows = [];
            return;
        }

        const componentId = this.selectedComponent.ComponentId?.toString() || '';

        this.deploymentRows = this.environments.map(env => {
            const build = this.selectedComponent!.environmentBuilds?.get(env.EnvironmentName!);
            
            if (!build) {
                return {
                    environmentName: env.EnvironmentName || '',
                    buildNumber: 'Not deployed',
                    status: 'N/A',
                    updateDate: 'N/A',
                    build: null,
                    componentId: componentId
                };
            }

            const updateDate = build.UpdateDate 
                ? new Date(build.UpdateDate).toLocaleDateString('en-GB', {
                    day: '2-digit',
                    month: '2-digit',
                    year: '2-digit'
                })
                : 'N/A';

            return {
                environmentName: env.EnvironmentName || '',
                buildNumber: build.RequestBuildNum || 'N/A',
                status: build.State || 'Unknown',
                updateDate: updateDate,
                requestId: build.RequestId,
                build: build,
                componentId: componentId
            };
        });

        this.requestUpdate('deploymentRows');
    }

    private applyFilter() {
        const childIds = this.collectAllChildIds(this.components);
        this.filteredComponents = this.flattenComponents(this.components, [], new Set<string>(), childIds);
    }

    private collectAllChildIds(components: ComponentDeploymentInfo[]): Set<string> {
        const childIds = new Set<string>();

        const collectChildren = (comps: ComponentDeploymentInfo[]) => {
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
        components: ComponentDeploymentInfo[],
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

    private buildNumberRenderer(
        root: HTMLElement,
        _column: GridColumn,
        model: GridItemModel<EnvironmentDeploymentRow>
    ) {
        const row = model.item;
        
        if (row.build && row.requestId) {
            root.innerHTML = `
                <span class="request-link build-number" data-request-id="${row.requestId}">
                    ${row.buildNumber}
                </span>
            `;
            
            const link = root.querySelector('.request-link');
            if (link) {
                link.addEventListener('click', () => {
                    window.open(`/monitor-result/${row.requestId}`, '_blank');
                });
            }
        } else if (row.buildNumber === 'Not deployed') {
            root.innerHTML = `<span class="no-deployment">${row.buildNumber}</span>`;
        } else {
            root.innerHTML = `<span class="build-number">${row.buildNumber}</span>`;
        }
    }

    private dateRenderer(
        root: HTMLElement,
        _column: GridColumn,
        model: GridItemModel<EnvironmentDeploymentRow>
    ) {
        const row = model.item;
        root.textContent = row.updateDate;
    }

    private statusRenderer(
        root: HTMLElement,
        _column: GridColumn,
        model: GridItemModel<EnvironmentDeploymentRow>
    ) {
        const row = model.item;
        const statusClass = this.getStatusClass(row.status);
        root.innerHTML = `<span class="${statusClass}">${row.status}</span>`;
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
