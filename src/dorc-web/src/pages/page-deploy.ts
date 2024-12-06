import '@vaadin/combo-box';
import { ComboBoxItemModel } from '@vaadin/combo-box';
import { ComboBox } from '@vaadin/combo-box/src/vaadin-combo-box';
import { css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
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
import { PageElement } from '../helpers/page-element';

@customElement('page-deploy')
export class PageDeploy extends PageElement {
  @property({ type: Array }) projects: ProjectApiModel[] = [];

  @property({ type: Array }) buildDefinitions: DeployArtefactDto[] = [];

  @property({ type: Object }) private project!: ProjectApiModel;

  @property({ type: Array }) environments:
    | Array<EnvironmentApiModel>
    | undefined = [];

  @property({ type: Boolean }) private projectsLoading = true;

  @property({ type: Boolean }) private envsLoading = false;

  @property({ type: String }) private selectedEnvironmentName = '';

  private environment: EnvironmentApiModel | undefined;

  static get styles() {
    return css`
      :host {
      }
      vaadin-combo-box {
        padding-top: 0px;
      }

      .scroller {
        overflow-y: auto;
        height: 100%;
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

    const api = new RefDataProjectsApi();
    api.refDataProjectsGet().subscribe(
      (data: ProjectApiModel[]) => {
        this.setProjects(data);
      },

      (err: any) => console.error(err),
      () => console.log('done loading projects')
    );
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
  }

  render() {
    return html`
      <div class="scroller">
        <table style="width: 330px">
          <tr>
            <td>
              <vaadin-combo-box
                @value-changed="${this._projectValueChanged}"
                .items="${this.projects}"
                .renderer="${this._projectsRenderer}"
                placeholder="Select Project"
                label="Project"
                style="width: 600px; padding-left: 10px"
                item-label-path="ProjectName"
                item-value-path="ProjectId"
                clear-button-visible
              ></vaadin-combo-box>
            </td>
            <td>
              ${this.projectsLoading
                ? html` <div class="small-loader"></div> `
                : html``}
            </td>
          </tr>
          <tr>
            <td>
              <vaadin-combo-box
                id="environments"
                @value-changed="${this._environmentValueChanged}"
                .items="${this.environments}"
                placeholder="Select Environment"
                label="Environment"
                style="width: 600px; padding-left: 10px"
                clear-button-visible
                item-label-path="EnvironmentName"
                item-value-path="EnvironmentId"
              ></vaadin-combo-box>
            </td>
            <td>
              ${this.envsLoading
                ? html` <div class="small-loader"></div> `
                : html``}
            </td>
          </tr>
        </table>
        <deploy-env
          .project="${this.project}"
          .envName="${this.selectedEnvironmentName}"
        ></deploy-env>
        <div></div>
      </div>
    `;
  }

  _projectsRenderer(
    root: HTMLElement,
    _comboBox: ComboBox,
    model: ComboBoxItemModel<ProjectApiModel>
  ) {
    const template = model.item as ProjectApiModel;
    root.innerHTML = `<div>${template.ProjectName}</div>`;
  }

  _projectValueChanged(data: any) {
    const projectId = data.target.value as number;
    const proj = this.projects?.find(value => value.ProjectId === projectId);
    if (proj) this.project = proj;

    this.clearComboboxSelectedItem('environments');

    if (this.project !== undefined) {
      this.envsLoading = true;
      const api = new RefDataProjectEnvironmentMappingsApi();
      api
        .refDataProjectEnvironmentMappingsGet({
          project: this.project?.ProjectName ?? '',
          includeRead: false
        })
        .subscribe({
          next: (
            templateApiModelEnvironmentApiModel: EnvironmentApiModelTemplateApiModel
          ) => {
            this.setEnvironments(templateApiModelEnvironmentApiModel);
          },
          error: (err: any) => {
            console.error(err);
          },
          complete: () => {
            console.log('done loading environments');
          }
        });
    }
  }

  _environmentValueChanged(data: any) {
    const envId = data.target.value as number;
    this.environment = this.environments?.find(
      value => value.EnvironmentId === envId
    );
    this.selectedEnvironmentName = this.environment?.EnvironmentName ?? '';
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
