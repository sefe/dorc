import '@vaadin/grid';
import type { GridItemModel } from '@vaadin/grid';
import '@vaadin/text-field';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import { css, PropertyValues, render } from 'lit';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/add-edit-project';
import { Notification } from '@vaadin/notification';
import { AddEditProject } from '../components/add-edit-project';
import '../components/grid-button-groups/project-controls';
import type { ProjectApiModel } from '../apis/dorc-api';
import { AccessControlType, SourceControlType } from '../apis/dorc-api';
import { RefDataProjectsApi } from '../apis/dorc-api/apis';
import { PageElement } from '../helpers/page-element';
import './page-project-envs';
import '../components/add-edit-access-control';
import { AddEditAccessControl } from '../components/add-edit-access-control';
import '../components/project-audit-data'
import { ProjectAuditData } from '../components/project-audit-data';
import '../components/confirm-dialog';
import { ConfirmDialog } from '../components/confirm-dialog';
import GlobalCache from '../global-cache';
import { ErrorNotification } from '../components/notifications/error-notification';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { SuccessNotification } from '../components/notifications/success-notification';

@customElement('page-projects-list')
export class PageProjectsList extends PageElement {
  @property({ type: Array }) projects: ProjectApiModel[] = [];

  @property({ type: Object }) selectedProject: ProjectApiModel = {};

  @property({ type: Array }) filteredProjects: ProjectApiModel[] = [];

  @property({ type: Array }) projectList = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property({ type: String }) project = '';

  @property({ type: String }) size = '';

  @query('#add-edit-project') addEditProject!: AddEditProject;

  @query('#open-project-audit-control') projectAuditData!: ProjectAuditData;

  @query('#confirm-delete-dialog') confirmDeleteDialog!: ConfirmDialog;

  @property({ type: String }) secureName = '';

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  public userRoles!: string[];

  private loading = true;

  constructor() {
    super();
    this.getUserRoles();
    this.getProjects();
  }

  private getUserRoles() {
    const gc = GlobalCache.getInstance();
    if (gc.userRoles === undefined) {
      gc.allRolesResp?.subscribe({
        next: (userRoles: string[]) => {
          this.setUserRoles(userRoles);
        }
      });
    } else {
      this.setUserRoles(gc.userRoles);
    }
  }

  private setUserRoles(userRoles: string[]) {
    this.userRoles = userRoles;
    this.isAdmin = this.userRoles.find(p => p === 'Admin') !== undefined;
    this.isPowerUser =
      this.userRoles.find(p => p === 'PowerUser') !== undefined;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'open-project-metadata',
      this.openProjectMetadata as EventListener
    );
    this.addEventListener(
      'open-access-control',
      this.openAccessControl as EventListener
    );
    this.addEventListener(
      'open-project-audit-data',
      this.openProjectAuditData as EventListener
    );
    this.addEventListener('project-added', this.projectAdded as EventListener);
    this.addEventListener(
      'project-updated',
      this.projectUpdated as EventListener
    );
    this.addEventListener(
      'delete-project',
      this.deleteProject as EventListener
    );
  }

  private getProjects() {
    const api = new RefDataProjectsApi();
    api.refDataProjectsGet().subscribe(
      (data: ProjectApiModel[]) => {
        this.setProjects(data);
      },

      (err: any) => console.error(err),
      () => console.log('done loading projects')
    );
  }

  static get styles() {
    return css`
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 115px);
        --divider-color: var(--dorc-border-color);
      }
      .overlay {
        width: 100%;
        height: 100%;
        position: fixed;
      }
      .overlay__inner {
        width: 100%;
        height: 100%;
        position: absolute;
      }
      .overlay__content {
        left: 20%;
        position: absolute;
        top: 20%;
        transform: translate(-50%, -50%);
      }
      .spinner {
        width: 75px;
        height: 75px;
        display: inline-block;
        border-width: 2px;
        border-color: var(--dorc-border-color);
        border-top-color: var(--dorc-link-color);
        animation: spin 1s infinite linear;
        border-radius: 100%;
        border-style: solid;
      }
      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }
      paper-dialog.size-position {
        top: 16px;
        overflow: auto;
        padding: 10px;
      }
    `;
  }

  render() {
    return html`<div style="display: inline">
        <vaadin-text-field
          style="padding-left: 5px; width: 50%;"
          placeholder="Search"
          @value-changed="${this.updateSearch}"
          clear-button-visible
          helper-text="Use | for multiple search terms"
        >
          <vaadin-icon slot="prefix" icon="vaadin:search"></vaadin-icon>
        </vaadin-text-field>
        <vaadin-button
          title="Add Project"
          style="width: 250px"
          @click="${this.addProject}"
        >
          <vaadin-icon
            icon="vaadin:archive"
            style="color: var(--dorc-link-color)"
          ></vaadin-icon
          >Add Project...
        </vaadin-button>
      </div>

      <add-edit-project
        .project="${this.selectedProject}"
        .projects="${this.projects}"
        id="add-edit-project"
      ></add-edit-project>

      <add-edit-access-control
        id="add-edit-access-control"
        .secureName="${this.secureName}"
      ></add-edit-access-control>

      <project-audit-data
        id="open-project-audit-control"
        .project="${this.selectedProject}"
      >
      </project-audit-data>

      ${this.loading
        ? html`
            <div class="overlay" style="z-index: 2">
              <div class="overlay__inner">
                <div class="overlay__content">
                  <span class="spinner"></span>
                </div>
              </div>
            </div>
          `
        : html`
            <vaadin-grid
              id="grid"
              .items=${this.filteredProjects}
              column-reordering-allowed
              multi-sort
              theme="compact row-stripes no-row-borders no-border"
            >
              <vaadin-grid-column
                width="50px"
                flex-grow="0"
                header=""
                .renderer="${this._sourceControlTypeRenderer}"
              ></vaadin-grid-column>
              <vaadin-grid-sort-column
                path="ProjectName"
                header="Name"
                resizable
                .renderer="${this._projectNameRenderer}"
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="ArtefactsUrl"
                header="Artefacts URL"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="ArtefactsSubPaths"
                header="Artefacts Sub-Paths"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-column
                .attachedPPLControl="${this}"
                .renderer="${this._projectEnvsButtonsRenderer}"
              ></vaadin-grid-column>
            </vaadin-grid>
          `} `;
  }

  openAccessControl(e: CustomEvent) {
    this.secureName = e.detail.Name as string;

    const addEditAccessControl = this.shadowRoot?.getElementById(
      'add-edit-access-control'
    ) as AddEditAccessControl;

    addEditAccessControl.open(this.secureName, AccessControlType.NUMBER_0);
  }

  openProjectAuditData(e: CustomEvent) {
    const project = e.detail.Project as ProjectApiModel;
    this.selectedProject = project;
    this.projectAuditData.project = project;
    this.projectAuditData.open();
    // this.addEditProject.project = project;
    // this.addEditProject.open();
  }

  _projectEnvsButtonsRenderer(
    root: HTMLElement,
    _column: HTMLElement,
    { item }: GridItemModel<ProjectApiModel>
  ) {
    const project = item as ProjectApiModel;
    // The below line has a horrible hack
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const altThis = _column.attachedPPLControl as PageProjectsList;
    render(
      html` <project-controls
        .project="${project}"
        .deleteHidden="${!altThis.isAdmin}"
      ></project-controls>`,
      root
    );
  }

  _sourceControlTypeRenderer(
    root: HTMLElement,
    _column: HTMLElement,
    { item }: GridItemModel<ProjectApiModel>
  ) {
    const project = item as ProjectApiModel;
    const size = '20';
    const color = 'var(--dorc-link-color)';

    // Determine type: use SourceControlType, but fall back to URL pattern for
    // projects that haven't been migrated yet (still showing default AzureDevOps=0)
    let effectiveType = project.SourceControlType ?? SourceControlType.AzureDevOps;
    if (effectiveType === SourceControlType.AzureDevOps &&
        project.ArtefactsUrl?.startsWith('file')) {
      effectiveType = SourceControlType.FileShare;
    }

    if (effectiveType === SourceControlType.GitHub) {
      // GitHub Invertocat logo
      render(html`<svg title="GitHub" width="${size}" height="${size}" viewBox="0 0 98 96" xmlns="http://www.w3.org/2000/svg" style="vertical-align: middle;">
        <path fill-rule="evenodd" clip-rule="evenodd" d="M48.854 0C21.839 0 0 22 0 49.217c0 21.756 13.993 40.172 33.405 46.69 2.427.49 3.316-1.059 3.316-2.362 0-1.141-.08-5.052-.08-9.127-13.59 2.934-16.42-5.867-16.42-5.867-2.184-5.704-5.42-7.17-5.42-7.17-4.448-3.015.324-3.015.324-3.015 4.934.326 7.523 5.052 7.523 5.052 4.367 7.496 11.404 5.378 14.235 4.074.404-3.178 1.699-5.378 3.074-6.6-10.839-1.141-22.243-5.378-22.243-24.283 0-5.378 1.94-9.778 5.014-13.2-.485-1.222-2.184-6.275.486-13.038 0 0 4.125-1.304 13.426 5.052a46.97 46.97 0 0 1 12.214-1.63c4.125 0 8.33.571 12.213 1.63 9.302-6.356 13.427-5.052 13.427-5.052 2.67 6.763.97 11.816.485 13.038 3.155 3.422 5.015 7.822 5.015 13.2 0 18.905-11.404 23.06-22.324 24.283 1.78 1.548 3.316 4.481 3.316 9.126 0 6.6-.08 11.897-.08 13.526 0 1.304.89 2.853 3.316 2.364 19.412-6.52 33.405-24.935 33.405-46.691C97.707 22 75.788 0 48.854 0z" fill="currentColor"/>
      </svg>`, root);
    } else if (effectiveType === SourceControlType.FileShare) {
      // Folder icon for file shares
      render(html`<vaadin-icon icon="vaadin:folder-open" title="File Share" style="color: ${color}; width: ${size}px; height: ${size}px;"></vaadin-icon>`, root);
    } else {
      // Azure DevOps logo
      render(html`<svg title="Azure DevOps" width="${size}" height="${size}" viewBox="0 0 18 18" xmlns="http://www.w3.org/2000/svg" style="vertical-align: middle;">
        <defs><linearGradient id="azdo-grad" x1="9" y1="16.97" x2="9" y2="1.03" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#0078d4"/><stop offset=".16" stop-color="#1380da"/><stop offset=".53" stop-color="#3c91e5"/><stop offset=".82" stop-color="#559cec"/><stop offset="1" stop-color="#5ea0ef"/></linearGradient></defs>
        <path d="M17 4v10l-4 3-6.5-2.3V17l-3-4 10.5.8V3.5zm-1 1.5L8.5 1v2.3L2.5 5 1 6.5v5l2.5 1V5.8z" fill="url(#azdo-grad)"/>
      </svg>`, root);
    }
  }

  _projectNameRenderer(
    root: HTMLElement,
    _column: HTMLElement,
    { item }: GridItemModel<ProjectApiModel>
  ) {
    const project = item as ProjectApiModel;
    render(
      html`<span title="${project.ProjectDescription || ''}">${project.ProjectName}</span>`,
      root
    );
  }

  sortProjects(a: ProjectApiModel, b: ProjectApiModel): number {
    if (String(a.ProjectName) > String(b.ProjectName)) return 1;
    return -1;
  }

  setProjects(projects: ProjectApiModel[]) {
    const sortedProjects = projects.sort(this.sortProjects);
    this.projects = sortedProjects;
    this.filteredProjects = sortedProjects;
    this.loading = false;
  }

  updateSearch(e: CustomEvent) {
    const value = (e.detail.value as string) || '';
    const filters = value
      .trim()
      .split('|')
      .map(filter => new RegExp(filter, 'i'));

    this.filteredProjects = this.projects.filter(
      ({
        ProjectName,
        ProjectDescription,
        ArtefactsBuildRegex,
        ArtefactsSubPaths
      }) =>
        filters.some(
          filter =>
            filter.test(ProjectName || '') ||
            filter.test(ProjectDescription || '') ||
            filter.test(ArtefactsBuildRegex || '') ||
            filter.test(ArtefactsSubPaths || '')
        )
    );
  }

  getEmptyProj(): ProjectApiModel {
    return {
      ProjectDescription: '',
      ProjectId: 0,
      ProjectName: '',
      ArtefactsBuildRegex: '',
      ArtefactsSubPaths: '',
      ArtefactsUrl: ''
    };
  }

  addProject() {
    this.selectedProject = this.getEmptyProj();
    this.addEditProject.open();
  }

  private openProjectMetadata(e: CustomEvent) {
    const project = e.detail.Project as ProjectApiModel;
    this.selectedProject = project;
    this.addEditProject.project = project;
    this.addEditProject.open();
  }

  projectAdded(e: CustomEvent) {
    this.getProjects();

    const project = e.detail.project as ProjectApiModel;
    Notification.show(`Project ${project.ProjectName} Created`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
    this.selectedProject = this.getEmptyProj();
    this.addEditProject.close();
  }

  projectUpdated(e: CustomEvent) {
    this.getProjects();
    const project = e.detail.project as ProjectApiModel;
    Notification.show(`Project ${project.ProjectName} Updated`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
    this.selectedProject = this.getEmptyProj();
    this.addEditProject.close();
  }

  private showSuccess(message: string) {
    const n = new SuccessNotification();
    n.setAttribute('successMessage', message);
    this.shadowRoot?.appendChild(n);
    n.open();
  }

  private showError(message: string) {
    const n = new ErrorNotification();
    n.setAttribute('errorMessage', message);
    this.shadowRoot?.appendChild(n);
    n.open();
  }

  private deleteProject(e: CustomEvent) {
    const project = e.detail.Project as ProjectApiModel;
    this.selectedProject = project;

    if (
      !confirm(
        `Are you sure you want to delete project "${project.ProjectName}"? This action cannot be undone.`
      )
    ) {
      this.selectedProject = this.getEmptyProj();
      return;
    }
    const id = project.ProjectId;
    if (typeof id !== 'number') {
      console.warn('Delete aborted: ProjectId is missing');
      this.selectedProject = this.getEmptyProj();
      return;
    }
    this.performDelete(id);
  }

  private performDelete(projectId: number) {
    const api = new RefDataProjectsApi();
    api.refDataProjectsProjectIdDelete({ projectId }).subscribe(
      (response: any) => {
        const backendMessage = retrieveErrorMessage(response);
        if (backendMessage) {
          this.showSuccess(backendMessage);
          this.getProjects();
        } else {
          console.error('Delete succeeded (no backend message)', response);
        }
      },
      (err: any) => {
        console.error(err);
        const backendMessage = retrieveErrorMessage(err);
        if (backendMessage) {
          this.showError(backendMessage);
        } else {
          console.error('Delete failed (no backend message)', err);
        }
      },
      () => {
        console.log(
          `Delete operation completed for ${this.selectedProject.ProjectName}`
        );
      }
    );
    this.selectedProject = this.getEmptyProj();
  }
}