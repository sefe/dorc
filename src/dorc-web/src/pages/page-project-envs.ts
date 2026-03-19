import { css, PropertyValues } from 'lit';
import '@vaadin/button';
import '@vaadin/icons';
import '@vaadin/icon';
import '@vaadin/vertical-layout';
import '@vaadin/horizontal-layout';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { Router } from '@vaadin/router';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { Notification } from '@vaadin/notification';
import { RefDataProjectsApi } from '../apis/dorc-api/apis';
import { AccessControlApi, AccessSecureApiModel, AccessControlType } from '../apis/dorc-api';
import '../components/attach-environment';
import '../components/environment-card.ts';
import { EnvironmentApiModel, ProjectApiModel } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { AddEditAccessControl } from '../components/add-edit-access-control';
import '../components/add-edit-access-control';
import GlobalCache from '../global-cache';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { AddEditProject } from '../components/add-edit-project';
import '../components/add-edit-project';
import {
  EnvironmentApiModelTemplateApiModel,
  RefDataProjectEnvironmentMappingsApi
} from '../apis/dorc-api';

@customElement('page-project-envs')
export class PageProjectEnvs extends PageElement {
  @property({ type: String })
  project: string | undefined;

  @property({ type: Array })
  environments: Array<EnvironmentApiModel> | undefined;

  private loading = true;

  @property({ type: String }) secureName = '';

  @property({ type: Object })
  private projectData: ProjectApiModel | undefined;

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  public userRoles!: string[];

  @state()
  private mapEnvDialogOpened = false;

  @state()
  private projects: ProjectApiModel[] | undefined;

  @state()
  private projectUserEditable = false;

  @query('#add-edit-project') addEditProject!: AddEditProject;

  static get styles() {
    return css`
      .card-element {
        padding: 10px;
        box-shadow: 1px 2px 3px rgba(0, 0, 0, 0.2);
        width: 300px;
        height: 126px;
        position: relative;
      }

      .card-element__heading {
        color: gray;
      }

      .card-element__text {
        color: gray;
        width: 180px;
        word-wrap: break-word;
        display: block;
        font-size: small;
      }

      .statistics-cards {
        max-width: 500px;
        display: flex;
        flex-wrap: wrap;
      }

      .statistics-cards__item {
        margin: 5px;
        flex-shrink: 0;
        background-color: #f5f6f8;
      }

      .environments {
        display: flex;
        flex-direction: row;
        flex-wrap: wrap;
        justify-content: flex-start;
        overflow-x: hidden;
        overflow-y: auto;
        height: calc(100vh - 50px);
      }

      a {
        color: blue;
        text-decoration: none; /* no underline */
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
        left: 30%;
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

      vaadin-button {
        padding: 2px;
      }
    `;
  }

  render() {
    return html`
      <div class="overlay" ?hidden="${!this.loading}">
        <div class="overlay__inner">
          <div class="overlay__content">
            <span class="spinner"></span>
          </div>
        </div>
      </div>
      <add-edit-access-control
        id="add-edit-access-control"
        .secureName="${this.secureName}"
      ></add-edit-access-control>
      <add-edit-project
        .project="${this.projectData}"
        .projects="${this.projects}"
        id="add-edit-project"
        @project-updated="${this.projectMetadataUpdated}"
      ></add-edit-project>
      <div class="environments">
        <div class="statistics-cards__item card-element">
          <div style="position: absolute; left: 10px; max-width: 250px">
            <h3 class="card-element__heading" style="margin: 0px">
              ${this.project}
            </h3>
            ${this.projectData?.ProjectDescription === '' ||
            this.projectData?.ProjectDescription === null ||
            this.projectData?.ProjectDescription === undefined
              ? html`<span class="card-element__text" style="font-style: italic"
                  >No Description</span
                >`
              : html`<span class="card-element__text"
                  >${this.projectData?.ProjectDescription}</span
                >`}
          </div>

          <div style="right: 8px; bottom: 8px; position: absolute;">
            <vaadin-vertical-layout style="gap: 8px; align-items: end;">
              <vaadin-horizontal-layout style="gap: 8px;">
                <vaadin-button
                  title="Attach Environment"
                  theme="icon"
                  @click="${this.openAttachEnv}"
                  style="margin: 0;"
                >
                  <vaadin-icon
                    icon="icons:link"
                    style="color: cornflowerblue"
                  ></vaadin-icon>
                </vaadin-button>
                <vaadin-button
                  title="Bundles"
                  theme="icon"
                  @click="${this.openBundles}"
                  style="margin: 0;"
                  ?hidden="${!this.projectUserEditable}"
                >
                  <vaadin-icon
                    icon="vaadin:package"
                    style="color: cornflowerblue"
                  ></vaadin-icon>
                </vaadin-button>
              </vaadin-horizontal-layout>
              <vaadin-horizontal-layout style="gap: 8px;">
                <vaadin-button
                  title="Reference Data"
                  theme="icon"
                  @click="${this.openRefData}"
                  style="margin: 0;"
                >
                  <vaadin-icon
                    icon="vaadin:curly-brackets"
                    style="color: cornflowerblue"
                  ></vaadin-icon>
                </vaadin-button>
                <vaadin-button
                  title="Edit Metadata..."
                  theme="icon"
                  @click="${this.openProjectMetadata}"
                  style="margin: 0;"
                >
                  <vaadin-icon
                    icon="lumo:edit"
                    style="color: cornflowerblue"
                  ></vaadin-icon>
                </vaadin-button>
              </vaadin-horizontal-layout>
            </vaadin-vertical-layout>
          </div>
        </div>
        ${this.environments?.map(
          env =>
            html` <environment-card
              .environment="${env}"
              .project="${this.project}"
              @envs-changed="${this.getEnvironments}"
            ></environment-card>`
        )}
      </div>
      <div style="padding-bottom: 20px"></div>

      <vaadin-dialog
        header-title="Map Environment to Project"
        .opened="${this.mapEnvDialogOpened}"
        @opened-changed="${(event: DialogOpenedChangedEvent) => {
          this.mapEnvDialogOpened = event.detail.value;
        }}"
        ${dialogRenderer(this.renderMapEnvDialog, [])}
        ${dialogFooterRenderer(this.renderMapEnvFooter, [])}
      ></vaadin-dialog>
    `;
  }

  constructor() {
    super();
    this.getProjects();
    this.getUserRoles();
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
    this.isPowerUser = this.userRoles.find(p => p === 'PowerUser') !== undefined;
  }

  private openProjectMetadata() {
    if (this.addEditProject) {
      this.addEditProject.project = this.projectData ?? {};
      this.addEditProject.open();
    }
  }

  private projectMetadataUpdated(e: CustomEvent) {
    this.projectData = e.detail.project as ProjectApiModel;
    Notification.show(`Project ${this.projectData.ProjectName} Updated`, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
    if (this.addEditProject) {
      this.addEditProject.close();
      Router.go(`project-envs/${this.addEditProject?.project?.ProjectName}`);
    }
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

  setProjects(projects: ProjectApiModel[]) {
    this.projects = projects;
  }

  openRefData() {
    const event = new CustomEvent('open-project-ref-data', {
      detail: {
        Project: this.projectData
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  openBundles() {
    Router.go(`project-envs/${this.project}/bundles`);
  }

  private renderMapEnvDialog = () => html`
    <attach-environment
      id="attach-environments"
      .projectName="${this.project ?? ''}"
      @attach-env-completed="${this.closeAttachEnv}"
    ></attach-environment>
  `;

  private renderMapEnvFooter = () => html`
    <vaadin-button @click="${this.close}">Close</vaadin-button>
  `;

  private close() {
    this.mapEnvDialogOpened = false;
  }

  openAttachEnv() {
    this.mapEnvDialogOpened = true;
  }

  closeAttachEnv() {
    this.getEnvironments();
    this.mapEnvDialogOpened = false;
  }

  public getEnvironments() {
    const api = new RefDataProjectEnvironmentMappingsApi();
    if (this.project !== undefined) {
      api
        .refDataProjectEnvironmentMappingsGet({
          project: this.project,
          includeRead: true
        })
        .subscribe(
          (data: EnvironmentApiModelTemplateApiModel) => {
            this.setEnvironments(data);
          },
          (err: any) => {
            console.error(err);
            Router.go('not-found');
          },
          () => {
            console.log('done loading environments');
            this.loading = false;
          }
        );
    }
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener('attach-envs', this.openAttachEnv);
    this.addEventListener(
      'open-access-control',
      this.openAccessControl as EventListener
    );

    const projectName = location.pathname.substring(
      location.pathname.lastIndexOf('/') + 1
    );
    this.project = decodeURIComponent(projectName);

    this.getEnvironments();
  }

  private setEnvironments(data: EnvironmentApiModelTemplateApiModel) {
    this.projectData = data.Project;
    this.environments = data.Items?.sort(this.sortEnvironments);
    this.checkProjectAccess();
  }

  private checkProjectAccess() {
    if (this.project) {
      const api = new AccessControlApi();
      api
        .accessControlGet({
          accessControlType: AccessControlType.NUMBER_0,
          accessControlName: this.project
        })
        .subscribe({
          next: (data: AccessSecureApiModel) => {
            this.projectUserEditable = data.UserEditable ?? false;
          },
          error: (err: string) => {
            console.error('Error fetching project access control:', err);
            this.projectUserEditable = false;
          }
        });
    }
  }

  sortEnvironments(a: EnvironmentApiModel, b: EnvironmentApiModel): number {
    if (String(a.EnvironmentName) > String(b.EnvironmentName)) return 1;
    return -1;
  }

  openAccessControl(e: CustomEvent) {
    this.secureName = e.detail.Name as string;
    const type = e.detail.Type as number;

    const addEditAccessControl = this.shadowRoot?.getElementById(
      'add-edit-access-control'
    ) as AddEditAccessControl;

    addEditAccessControl.open(this.secureName, type);
  }
}
