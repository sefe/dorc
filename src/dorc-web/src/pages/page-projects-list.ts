import { columnBodyRenderer } from '@vaadin/grid/lit';
import { confirmPrompt } from '../components/confirm-prompt';
import '@vaadin/grid';
import '../components/dorc-spinner';
import '@vaadin/text-field';
import '@vaadin/icon';
import '../icons/dorc-icons.js';
import { css, PropertyValues } from 'lit';
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
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import './page-project-envs';
import '../components/add-edit-access-control';
import { AddEditAccessControl } from '../components/add-edit-access-control';
import GlobalCache from '../global-cache';
import { ErrorNotification } from '../components/notifications/error-notification';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { SuccessNotification } from '../components/notifications/success-notification';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

@customElement('page-projects-list')
export class PageProjectsList extends ResponsiveMixin(PageElement) {
  @property({ type: Array }) projects: ProjectApiModel[] = [];

  @property({ type: Object }) selectedProject: ProjectApiModel = {};

  @property({ type: Array }) filteredProjects: ProjectApiModel[] = [];

  @property({ type: Array }) projectList = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property({ type: String }) project = '';

  @property({ type: String }) size = '';

  @query('#add-edit-project') addEditProject!: AddEditProject;

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
    this.addEventListener('project-added', this.projectAdded as EventListener);
    this.addEventListener(
      'project-updated',
      this.projectUpdated as EventListener
    );
    this.addEventListener(
      'delete-project',
      this.deleteProject as unknown as EventListener
    );
  }

  private getProjects() {
    const api = new RefDataProjectsApi(dorcApiConfiguration);
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
      :host {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;
        --divider-color: var(--dorc-border-color);
      }
      vaadin-grid#grid {
        flex: 1;
        min-height: 0;
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
          <vaadin-icon slot="prefix" icon="dorc:search"></vaadin-icon>
        </vaadin-text-field>
        <vaadin-button
          title="Add Project"
          style="width: 250px"
          @click="${this.addProject}"
        >
          <vaadin-icon
            icon="dorc:plus"
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

      ${
        this.loading
          ? html` <dorc-spinner></dorc-spinner> `
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
                  ${columnBodyRenderer(this._sourceControlTypeRenderer, [])}
                ></vaadin-grid-column>
                <vaadin-grid-sort-column
                  path="ProjectName"
                  header="Name"
                  resizable
                  ${columnBodyRenderer(this._projectNameRenderer, [])}
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  path="ProjectDescription"
                  header="Project Description"
                  resizable
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  path="ArtefactsUrl"
                  header="Artefacts URL"
                  resizable
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  path="ArtefactsSubPaths"
                  header="Artefacts Sub-Paths"
                  resizable
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  path="ArtefactsBuildRegex"
                  header="Build Definition Regex"
                  resizable
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  path="LeanIXUrl"
                  header="LeanIX URL"
                  resizable
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-column
                  width="180px"
                  ${columnBodyRenderer(this._projectEnvsButtonsRenderer, [
                    this.isAdmin
                  ])}
                ></vaadin-grid-column>
              </vaadin-grid>
            `
      } `;
  }

  openAccessControl(e: CustomEvent) {
    this.secureName = e.detail.Name as string;

    const addEditAccessControl = this.shadowRoot?.getElementById(
      'add-edit-access-control'
    ) as AddEditAccessControl;

    addEditAccessControl.open(this.secureName, AccessControlType.Project);
  }

  _projectEnvsButtonsRenderer(item: ProjectApiModel) {
    const project = item as ProjectApiModel;
    return html` <project-controls
      .project="${project}"
      .deleteHidden="${!this.isAdmin}"
    ></project-controls>`;
  }

  _sourceControlTypeRenderer(item: ProjectApiModel) {
    const project = item as ProjectApiModel;
    const size = '20';
    const color = 'var(--dorc-link-color)';

    // API returns enum as string ("AzureDevOps", "GitHub", "FileShare")
    const sct = String(project.SourceControlType ?? '');
    const isFileUrl =
      project.ArtefactsUrl?.toLowerCase().startsWith('file') ?? false;
    const isGitHub = sct === 'GitHub';
    const isFileShare = sct === 'FileShare' || isFileUrl;

    if (isGitHub) {
      // GitHub Invertocat logo
      return html`<vaadin-icon
        icon="dorc:brand-github"
        title="GitHub"
        aria-label="GitHub"
        style="color: currentColor; width: ${size}px; height: ${size}px; vertical-align: middle;"
      ></vaadin-icon>`;
    } else if (isFileShare) {
      // Folder icon for file shares
      return html`<vaadin-icon
        icon="dorc:folder-open"
        title="File Share"
        style="color: ${color}; width: ${size}px; height: ${size}px;"
      ></vaadin-icon>`;
    } else {
      // Azure DevOps logo
      return html`<vaadin-icon
        icon="dorc:brand-azure-devops"
        title="Azure DevOps"
        aria-label="Azure DevOps"
        style="color: ${color}; width: ${size}px; height: ${size}px; vertical-align: middle;"
      ></vaadin-icon>`;
    }
  }

  _projectNameRenderer(item: ProjectApiModel) {
    const project = item as ProjectApiModel;
    return html`<span title="${project.ProjectDescription || ''}"
      >${project.ProjectName}</span
    >`;
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
        ArtefactsSubPaths,
        LeanIXUrl
      }) =>
        filters.some(
          filter =>
            filter.test(ProjectName || '') ||
            filter.test(ProjectDescription || '') ||
            filter.test(ArtefactsBuildRegex || '') ||
            filter.test(ArtefactsSubPaths || '') ||
            filter.test(LeanIXUrl || '')
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
      ArtefactsUrl: '',
      LeanIXUrl: ''
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

  private async deleteProject(e: CustomEvent) {
    const project = structuredClone(e.detail.Project as ProjectApiModel);
    this.selectedProject = project;

    if (
      !(await confirmPrompt(
        `Are you sure you want to delete project "${project.ProjectName}"? This action cannot be undone.`
      ))
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
    this.performDelete(id, project.Revision ?? undefined);
  }

  private performDelete(projectId: number, expectedRevision?: string) {
    const api = new RefDataProjectsApi(dorcApiConfiguration);
    api
      .refDataProjectsProjectIdDelete({ projectId, expectedRevision })
      .subscribe(
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
