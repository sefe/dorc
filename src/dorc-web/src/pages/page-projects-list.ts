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
import { AccessControlType } from '../apis/dorc-api';
import { RefDataProjectsApi } from '../apis/dorc-api/apis';
import { PageElement } from '../helpers/page-element';
import './page-project-envs';
import '../components/add-edit-access-control';
import { AddEditAccessControl } from '../components/add-edit-access-control';
import '../components/project-audit-data';
import { ProjectAuditData } from '../components/project-audit-data';
import '../components/confirm-dialog';
import { ConfirmDialog } from '../components/confirm-dialog';
import GlobalCache from '../global-cache';

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
    this.addEventListener(
      'confirm-dialog-confirm',
      this.confirmDeleteProject as EventListener
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
        --divider-color: rgb(223, 232, 239);
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
        border-color: rgba(255, 255, 255, 0.05);
        border-top-color: cornflowerblue;
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
            style="color: cornflowerblue"
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

      <confirm-dialog
        id="confirm-delete-dialog"
        title="Delete Project"
        message="Are you sure you want to delete this project? This action cannot be undone."
        confirmText="Delete"
        cancelText="Cancel"
      >
      </confirm-dialog>

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
              <vaadin-grid-sort-column
                path="ProjectName"
                header="Name"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="ProjectDescription"
                header="Project Description"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="ArtefactsUrl"
                header="Azure DevOps Url"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="ArtefactsSubPaths"
                header="Azure DevOps Project(s)"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="ArtefactsBuildRegex"
                header="Build Definition Regex"
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

  private deleteProject(e: CustomEvent) {
    const project = e.detail.Project as ProjectApiModel;
    this.selectedProject = project;
    this.confirmDeleteDialog.open();
  }

  private confirmDeleteProject() {
    if (!this.selectedProject || !this.selectedProject.ProjectId) {
      return;
    }

    const api = new RefDataProjectsApi();
    const projectId = this.selectedProject.ProjectId;
    const projectName = this.selectedProject.ProjectName;

    api.refDataProjectsDelete({ projectId }).subscribe({
      next: () => {
        this.getProjects();
        Notification.show(`Project ${projectName} deleted successfully`, {
          theme: 'success',
          position: 'bottom-start',
          duration: 5000
        });
      },
      error: (error: any) => {
        console.error('Error deleting project:', error);
        const errorMessage =
          error.response?.text || error.message || 'Failed to delete project';
        Notification.show(`Error deleting project: ${errorMessage}`, {
          theme: 'error',
          position: 'bottom-start',
          duration: 10000
        });
      },
      complete: () => {
        console.log('Delete operation completed');
      }
    });

    this.selectedProject = this.getEmptyProj();
  }
}
