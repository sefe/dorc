import { comboBoxRenderer } from '@vaadin/combo-box/lit';
import '@vaadin/combo-box';
import { ComboBox } from '@vaadin/combo-box';
import { css, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { DeployEnv } from '../components/deploy/deploy-env';
import '../components/deploy/deploy-env';
import {
  DeployArtefactDto,
  EnvironmentApiModelTemplateApiModel
} from '../apis/dorc-api';
import {
  EnvironmentApiModel,
  RefDataProjectEnvironmentMappingsApi,
  RefDataProjectsApi
} from '../apis/dorc-api';
import type { ProjectApiModel } from '../apis/dorc-api';
import GlobalCache from '../global-cache';
import { PageElement } from '../helpers/page-element';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import {
  DEPLOY_MASCOT_THEMES,
  getDeployMascotIconPath,
  getDeployMascotThemeOverride,
  MascotTheme,
  setDeployMascotThemeOverride
} from '../helpers/mascot-icon';

@customElement('page-deploy')
export class PageDeploy extends PageElement {
  @property({ type: Array }) projects: ProjectApiModel[] = [];

  @property({ type: Array }) buildDefinitions: DeployArtefactDto[] = [];

  @property({ type: Object }) project: ProjectApiModel | undefined;

  @property({ type: Array }) environments:
    Array<EnvironmentApiModel> | undefined = [];

  @property({ type: Boolean }) projectsLoading = true;

  @property({ type: Boolean }) envsLoading = false;
  @property({ type: String }) selectionError = '';
  @property({ type: Boolean }) isAdmin = false;
  @property({ type: Boolean }) themeSwitcherVisible = false;

  @state() private mascotTheme: MascotTheme = getDeployMascotThemeOverride();

  private readonly boundKeyDown = (e: KeyboardEvent) => {
    if (
      this.isAdmin &&
      e.ctrlKey &&
      e.shiftKey &&
      e.key.toLowerCase() === 'm'
    ) {
      e.preventDefault();
      this.themeSwitcherVisible = !this.themeSwitcherVisible;
    }
  };

  @property({ type: String }) selectedEnvironmentName = '';
  @property({ type: String }) selectedProjectId =
    new URLSearchParams(window.location.search).get('projectId') ?? '';
  private readonly initialProjectId = Number(this.selectedProjectId);
  private readonly initialComponentId = Number(
    new URLSearchParams(window.location.search).get('componentId')
  );
  private initialSelectionApplied = false;

  private environment: EnvironmentApiModel | undefined;

  static get styles() {
    return css`
      :host {
      }
      vaadin-combo-box {
        padding-top: 0px;
      }

      .scroller {
        position: relative;
        overflow-y: auto;
        height: 100%;
      }

      .deploy-selector-controls {
        display: flex;
        flex-direction: column;
        gap: var(--lumo-space-xs);
        max-width: 600px;
        margin-left: 10px;
      }

      .deploy-mascot-pane {
        position: absolute;
        top: 0;
        left: 620px;
        right: 0;
        height: 360px;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: var(--lumo-space-xs);
        pointer-events: none;
      }

      .cover {
        height: 220px;
        width: auto;
        object-fit: contain;
        flex-shrink: 0;
      }

      .mascot-theme-switcher {
        display: inline-flex;
        align-items: center;
        gap: 2px;
        padding: 2px 4px;
        border-radius: 999px;
        background: var(--lumo-contrast-5pct);
        border: 1px solid var(--lumo-contrast-10pct);
        pointer-events: auto;
      }

      .mascot-theme-btn {
        appearance: none;
        border: none;
        background: transparent;
        color: var(--lumo-secondary-text-color);
        font-family: inherit;
        font-size: var(--lumo-font-size-xxs);
        font-weight: 500;
        padding: 2px 8px;
        border-radius: 999px;
        cursor: pointer;
        line-height: 1.4;
      }

      .mascot-theme-btn:hover {
        color: var(--lumo-body-text-color);
        background: var(--lumo-contrast-10pct);
      }

      .mascot-theme-btn[aria-pressed='true'] {
        background: var(--lumo-primary-color);
        color: var(--lumo-primary-contrast-color);
      }

      @media (max-width: 900px) {
        .deploy-mascot-pane {
          display: none;
        }
      }

      .small-loader {
        border: 2px solid #f3f3f3; /* Light grey */
        border-top: 2px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
      }

      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }
    `;
  }

  constructor() {
    super();

    this.loadUserRoles();

    const api = new RefDataProjectsApi(dorcApiConfiguration);
    api.refDataProjectsGet().subscribe(
      (data: ProjectApiModel[]) => {
        this.setProjects(data);
      },

      error => {
        this.projectsLoading = false;
        this.selectionError =
          retrieveErrorMessage(error) ||
          'Could not load deployment projects. Reload to try again.';
      },
      () => console.log('done loading projects')
    );
  }

  connectedCallback() {
    super.connectedCallback();
    window.addEventListener('keydown', this.boundKeyDown);
  }

  disconnectedCallback() {
    window.removeEventListener('keydown', this.boundKeyDown);
    super.disconnectedCallback();
  }

  private loadUserRoles() {
    const gc = GlobalCache.getInstance();
    if (gc.userRoles === undefined) {
      gc.allRolesResp?.subscribe({
        next: (userRoles: string[]) => {
          this.isAdmin = userRoles.includes('Admin');
        }
      });
    } else {
      this.isAdmin = gc.userRoles.includes('Admin');
    }
  }

  private selectMascotTheme(theme: MascotTheme) {
    this.mascotTheme = theme;
    setDeployMascotThemeOverride(theme);
  }

  sortProjects(a: ProjectApiModel, b: ProjectApiModel): number {
    if (String(a.ProjectName) > String(b.ProjectName)) return 1;
    return -1;
  }

  sortBuildDefinitions(a: DeployArtefactDto, b: DeployArtefactDto): number {
    if (String(a.Name) > String(b.Name)) return 1;
    return -1;
  }

  setProjects(projects: ProjectApiModel[]) {
    const sortedProjects = projects.sort(this.sortProjects);
    this.projects = sortedProjects;
    this.projectsLoading = false;
    if (this.initialProjectId && !this.initialSelectionApplied) {
      this.initialSelectionApplied = true;
      this.selectProject(this.initialProjectId);
    }
  }

  render() {
    return html`
      <div class="scroller">
        ${this.selectionError ? html`<p role="alert">${this.selectionError}</p>` : ''}
        <div
          class="deploy-selector-controls"
          style="display: flex; flex-direction: column; gap: var(--lumo-space-xs); max-width: 600px; margin-left: 10px;"
        >
          <div
            style="display: flex; align-items: center; gap: var(--lumo-space-s);"
          >
            <vaadin-combo-box
              style="flex: 1;"
              @change="${this._projectValueChanged}"
              .items="${this.projects}"
              .selectedItem="${this.project}"
              ${comboBoxRenderer(this._projectsRenderer, [])}
              placeholder="Select Project"
              label="Project"
              item-label-path="ProjectName"
              item-value-path="ProjectId"
              clear-button-visible
            ></vaadin-combo-box>
            ${
              this.projectsLoading
                ? html` <div class="small-loader"></div> `
                : html``
            }
          </div>
          <div
            style="display: flex; align-items: center; gap: var(--lumo-space-s);"
          >
            <vaadin-combo-box
              id="environments"
              style="flex: 1;"
              @value-changed="${this._environmentValueChanged}"
              .items="${this.environments}"
              placeholder="Select Environment"
              label="Environment"
              clear-button-visible
              item-label-path="EnvironmentName"
              item-value-path="EnvironmentId"
            ></vaadin-combo-box>
            ${
              this.envsLoading
                ? html` <div class="small-loader"></div> `
                : html``
            }
          </div>
        </div>
        <div class="deploy-mascot-pane">
          <img
            class="cover"
            src="${getDeployMascotIconPath(new Date(), this.mascotTheme)}"
            alt="Wait to Deploy"
          />
          ${
            this.isAdmin && this.themeSwitcherVisible
              ? html`
                  <div
                    class="mascot-theme-switcher"
                    role="group"
                    aria-label="Mascot theme preview"
                  >
                    ${DEPLOY_MASCOT_THEMES.map(
                      theme => html`
                        <button
                          type="button"
                          class="mascot-theme-btn"
                          aria-pressed="${
                            this.mascotTheme === theme.id ? 'true' : 'false'
                          }"
                          @click="${() => this.selectMascotTheme(theme.id)}"
                        >
                          ${theme.label}
                        </button>
                      `
                    )}
                  </div>
                `
              : nothing
          }
        </div>
        <deploy-env
          id="deployEnv"
          .preselectedComponentId="${this.project?.ProjectId === this.initialProjectId ? this.initialComponentId : 0}"
          .project="${this.project}"
          .envName="${this.selectedEnvironmentName}"
        ></deploy-env>
        <div></div>
      </div>
    `;
  }

  _projectsRenderer(project: ProjectApiModel) {
    return html`<div>${project.ProjectName}</div>`;
  }

  _projectValueChanged(data: any) {
    if (this.projectsLoading) return;
    this.selectProject(Number(data.target.value));
  }

  private selectProject(projectId: number) {
    this.selectedProjectId = projectId ? String(projectId) : '';
    const proj = this.projects?.find(value => value.ProjectId === projectId);
    if (proj === this.project) return;
    this.project = proj;

    this.clearComboboxSelectedItem('environments');
    this.environments = [];
    this.selectedEnvironmentName = '';
    this.selectionError = '';

    if (this.project !== undefined) {
      const project = this.project;
      this.envsLoading = true;
      const api = new RefDataProjectEnvironmentMappingsApi(
        dorcApiConfiguration
      );
      api
        .refDataProjectEnvironmentMappingsGet({
          project: this.project?.ProjectName ?? '',
          includeRead: false
        })
        .subscribe({
          next: (
            templateApiModelEnvironmentApiModel: EnvironmentApiModelTemplateApiModel
          ) => {
            if (this.project !== project) return;
            this.setEnvironments(templateApiModelEnvironmentApiModel);
          },
          error: (err: any) => {
            if (this.project !== project) return;
            this.envsLoading = false;
            this.selectionError =
              retrieveErrorMessage(err) ||
              'Could not load deployable environments.';
          },
          complete: () => {
            console.log('done loading environments');
          }
        });
    } else {
      this.envsLoading = false;
    }
  }

  _environmentValueChanged(data: any) {
    const envId = Number(data.target.value);
    this.environment = this.environments?.find(
      value => value.EnvironmentId === envId
    );
    this.selectedEnvironmentName = this.environment?.EnvironmentName ?? '';
    const deployEnv = this.shadowRoot?.getElementById('deployEnv') as DeployEnv;
    deployEnv.EnvironmentChange(this.selectedEnvironmentName);
  }

  private setEnvironments(data: EnvironmentApiModelTemplateApiModel) {
    this.environments = data.Items?.sort(this.sortEnvironments);
    const envCombo = this.shadowRoot?.getElementById(
      'environments'
    ) as ComboBox;
    if (envCombo && this.environments) {
      envCombo.selectedItem = this.environments[0];
    }
    this.envsLoading = false;
  }

  sortEnvironments(a: EnvironmentApiModel, b: EnvironmentApiModel): number {
    if (String(a.EnvironmentName) > String(b.EnvironmentName)) return 1;
    return -1;
  }

  private clearComboboxSelectedItem(comboName: string) {
    const combo = this.shadowRoot?.getElementById(comboName) as ComboBox;
    if (combo) combo.selectedItem = undefined;
  }
}
