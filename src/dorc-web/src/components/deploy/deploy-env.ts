import '@vaadin/button';
import '@vaadin/combo-box';
import { ComboBoxItemModel } from '@vaadin/combo-box';
import { ComboBox } from '@vaadin/combo-box/src/vaadin-combo-box';
import '@vaadin/details';
import '@vaadin/dialog';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/horizontal-layout';
import '@vaadin/notification';
import '@vaadin/text-field';
import { TextField } from '@vaadin/text-field';
import { css, LitElement, PropertyValues, render } from 'lit';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PropertiesApi, RequestApi } from '../../apis/dorc-api';
import type { RequestPostRequest } from '../../apis/dorc-api';
import {
  DeployArtefactDto,
  DeployComponentDto,
  PropertyApiModel,
  RequestProperty,
  RequestStatusDto
} from '../../apis/dorc-api';
import type { ProjectApiModel } from '../../apis/dorc-api';
import './deploy-confirm-dialog';
import './property-override-controls';
import { ErrorNotification } from '../notifications/error-notification';
import './component-tree/hegs-tree';
import { HegsTree } from './component-tree/hegs-tree';
import { TreeNode } from './component-tree/TreeNode';
import { SuccessfulDeployNotification } from './notifications/successful-deploy-notification';
import { DeployConfirmDialog } from './deploy-confirm-dialog';

@customElement('deploy-env')
export class DeployEnv extends LitElement {
  private _project!: ProjectApiModel;

  @property({ type: Object })
  get project(): ProjectApiModel {
    return this._project;
  }

  set project(value: ProjectApiModel) {
    const oldValue = this._project;
    this._project = value;
    this.requestUpdate('project', oldValue);

    if (oldValue !== this._project) this.loadBuildDefinitions();
  }

  @property({ type: Array }) buildDefinitions: DeployArtefactDto[] = [];

  @property({ type: Array }) builds: DeployArtefactDto[] = [];

  @property({ type: String }) envName = '';

  @property({ type: Array }) data: TreeNode[];

  @property({ type: Array }) propertyOverrides: RequestProperty[] = [];

  @property({ type: Array }) properties: PropertyApiModel[] | undefined;

  @property({ type: Boolean }) private buildDefsLoading = false;

  @property({ type: Boolean }) private buildsLoading = false;

  @property({ type: Boolean }) private isFolderProject = false;

  @property({ type: String }) selectedBuildId: string | undefined;

  @property({ type: Number }) lastDeploymentId = 0;

  @property({ type: Boolean }) deploymentStarting = false;

  @property() ErrorMessage = '';

  @property({ type: Object }) req!: RequestPostRequest;

  @query('#dialog') dialog!: DeployConfirmDialog;

  @state()
  dialogOpened = false;

  @state() private requestedDeployment: RequestStatusDto | undefined;

  static get styles() {
    return css`
        :host{
            overflow-y: scroll;
        }
      vaadin-combo-box {
        padding-top: 0px;
      }
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(30vh - 110px);
        --divider-color: rgb(223, 232, 239);
      }
      .small-loader {
        border: 2px solid #f3f3f3; /* Light grey */
        border-top: 2px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
      }
      .loader {
        border: 16px solid #f3f3f3; /* Light grey */
        border-top: 16px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 120px;
        height: 120px;
        animation: spin 2s linear infinite;
      }
      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }

    `;
  }

  private buildDef = '';

  private lastProjectIdBuildDefs = 0;

  private propertyName = '';

  private propertyValue = '';

  private selectedBuild = '';

  constructor() {
    super();
    this.data = [];
  }

  sortBuildDefinitions(a: DeployArtefactDto, b: DeployArtefactDto): number {
    if (String(a.Name) > String(b.Name)) return 1;
    return -1;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'deploy-confirm-dialog-closed',
      this.deployConfirmDialogClosed as EventListener
    );
    this.addEventListener(
      'deploy-confirm-dialog-begin',
      this.startDeployment as EventListener
    );

    const api = new PropertiesApi();
    api.propertiesGet().subscribe({
      next: (data: PropertyApiModel[]) => {
        this.properties = data;
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading properties')
    });
  }

  render() {
    return html`
      <deploy-confirm-dialog
        id="dialog"
        .deployJson="${this.req}"
      ></deploy-confirm-dialog>
      <table
        style="width: 330px; margin-left: 10px"
        ?hidden="${this.isFolderProject}"
      >
        <tr>
          <td>
            <vaadin-combo-box
              id="build-defs"
              @value-changed="${this._buildDefValueChanged}"
              .items="${this.buildDefinitions}"
              .renderer="${this._buildRenderer}"
              placeholder="Select Build Definition"
              label="Build Definition"
              style="width: 600px"
              clear-button-visible
              item-label-path="Name"
              item-value-path="Name"
            ></vaadin-combo-box>
          </td>
          <td>
            ${this.buildDefsLoading
              ? html` <div class="small-loader"></div> `
              : html``}
          </td>
        </tr>
        <tr>
          <td>
            <vaadin-combo-box
              id="builds"
              @value-changed="${this._buildValueChanged}"
              .items="${this.builds}"
              .renderer="${this._buildRenderer}"
              placeholder="Select Build Number"
              label="Build Number"
              style="width: 600px"
              clear-button-visible
              item-label-path="Name"
              item-value-path="Name"
            ></vaadin-combo-box>
          </td>
          <td>
            ${this.buildsLoading
              ? html` <div class="small-loader"></div> `
              : html``}
          </td>
        </tr>
      </table>
      <table
        style="width: 330px; margin-left: 10px"
        ?hidden="${!this.isFolderProject}"
      >
        <tr>
          <td>
            <vaadin-combo-box
              id="folders"
              @value-changed="${this._buildValueChanged}"
              .items="${this.builds}"
              .renderer="${this._buildRenderer}"
              placeholder="Select Folder"
              label="Folder Artifacts"
              style="width: 600px"
              clear-button-visible
              item-label-path="Name"
              item-value-path="Name"
            ></vaadin-combo-box>
          </td>
          <td>
            ${this.buildsLoading
              ? html` <div class="small-loader"></div> `
              : html``}
          </td>
        </tr>
      </table>
      <vaadin-details
        opened
        summary="Components"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; padding-left: 10px"
      >
        <hegs-tree id="hegs-tree" .data="${this.data}"></hegs-tree>
      </vaadin-details>
      <vaadin-details
        closed
        summary="Property Overrides (Optional)"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; padding-left: 10px"
      >
        <vaadin-vertical-layout style="align-items: stretch">
          <vaadin-combo-box
            @value-changed="${this._propNameValueChanged}"
            .items="${this.properties}"
            placeholder="Select Property"
            clear-button-visible
            item-label-path="Name"
            item-value-path="Name"
            style="min-width: 600px"
          ></vaadin-combo-box>
          <vaadin-text-field
            required
            placeholder="Property Value"
            @value-changed="${this._propValueChanged}"
            style="min-width: 500px"
          ></vaadin-text-field>
          <vaadin-button
            @click="${this.AddOverrideProperty}"
            style="width: 96px"
            theme="primary"
            >Add
          </vaadin-button>
          <vaadin-grid
            id="grid"
            .items="${this.propertyOverrides}"
            column-reordering-allowed
            multi-sort
            theme="compact row-stripes no-row-borders no-border"
          >
            <vaadin-grid-sort-column
              header="Property Name"
              path="PropertyName"
              width="300px"
              flex-grow="0"
              resizable
            ></vaadin-grid-sort-column>
            <vaadin-grid-sort-column
              header="Property Value"
              path="PropertyValue"
              flex-grow="0"
              width="300px"
              resizable
            ></vaadin-grid-sort-column>
            <vaadin-grid-column
              .renderer="${this._boundPropOverridesButtonsRenderer}"
              .attachedDbsControl="${this}"
              resizable
            ></vaadin-grid-column>
          </vaadin-grid>
        </vaadin-vertical-layout>
      </vaadin-details>
      <vaadin-button
        style="width: 600px; margin-left: 12px; margin-bottom: 50px"
        @click="${this.openDeployDialog}"
        theme="primary"
        >Deploy
      </vaadin-button>
      ${this.deploymentStarting ? html` <div class="loader"></div> ` : html``}
      <div style="color: #FF3131">${this.ErrorMessage}</div>
    `;
  }

  _boundPropOverridesButtonsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<RequestProperty>
  ) {
    const propertyOverride = model.item as RequestProperty;

    // The below line has a horrible hack
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const altThis = _column.attachedDbsControl as DeployEnv;
    render(
      html`<property-override-controls
        .propertyOverride="${propertyOverride}"
        @property-override-removed="${() => {
          altThis.removePropertyOverride(propertyOverride);
        }}"
      ></property-override-controls>`,
      root
    );
  }

  _buildRenderer(
    root: HTMLElement,
    _comboBox: ComboBox,
    model: ComboBoxItemModel<DeployArtefactDto>
  ) {
    const template = model.item as DeployArtefactDto;

    render(
      html`
        <vaadin-horizontal-layout>
          ${template.Name?.replace('[PINNED]', '')}
          ${template.Name?.includes('[PINNED]')
            ? html`<dorc-icon icon="settings"></dorc-icon>`
            : html``}
        </vaadin-horizontal-layout>
      `,
      root
    );
  }

  setBuildDefinitions(projects: DeployArtefactDto[]) {
    const sortedBuildDefinitions = projects.sort(this.sortBuildDefinitions);
    this.buildDefinitions = sortedBuildDefinitions;
    if (
      this.buildDefinitions[0].Name === 'Not an Azure DevOps Server Project'
    ) {
      this.isFolderProject = true;
    } else {
      this.isFolderProject = false;
    }
    if (this.buildDefinitions.length > 0) {
      const buildDefs = this.shadowRoot?.getElementById(
        'build-defs'
      ) as ComboBox;
      if (buildDefs) {
        buildDefs.selectedItem = this.buildDefinitions[0].Name ?? '';
      }
    }
    this.buildDefsLoading = false;
  }

  deployConfirmDialogClosed() {
    this.dialogOpened = false;
  }

  private setBuilds(data: DeployArtefactDto[]) {
    this.builds = data;
    if (this.builds.length > 0) {
      const itemComboBox = this.shadowRoot?.getElementById(
        'builds'
      ) as ComboBox;
      if (itemComboBox) {
        itemComboBox.selectedItem = this.builds[0].Name ?? '';
      }
      this.buildsLoading = false;
    }
  }

  public EnvironmentChange(env: string) {
    this.envName = env;
    if (this._project !== undefined) {
      this.LoadBuilds();
    }
  }

  _buildDefValueChanged(data: any) {
    this.buildDef = data.target.value as string;
    if (this._project !== undefined) {
      this.LoadBuilds();
    }
  }

  private LoadBuilds() {
    this.buildsLoading = true;
    const api = new RequestApi();
    api
      .requestBuildsGet({
        projectId: this._project?.ProjectId ?? 0,
        environment: this.envName,
        buildDefinitionName: this.buildDef
      })
      .subscribe({
        next: (deployArtefactDtos: DeployArtefactDto[]) => {
          this.setBuilds(deployArtefactDtos);
        },
        error: (err: any) => {
          console.error(err);

          const notification = new ErrorNotification();
          const message =
            err.response.Message ?? err.response.ExceptionMessage;
          if (message) {
            notification.setAttribute('errorMessage', message);
          } else {
            notification.setAttribute('errorMessage', err.response);
          }
          this.shadowRoot?.appendChild(notification);
          notification.open();
          this.buildsLoading = false;
        },
        complete: () => console.log('done loading build definitions')
      });
  }

  getProjectComponents() {
    const tree = this.shadowRoot?.getElementById('hegs-tree') as HegsTree;
    if (tree) tree.componentsLoading = true;
    const reqApi = new RequestApi();
    reqApi
      .requestComponentsGet({ projectId: this._project?.ProjectId ?? 0 })
      .subscribe(
        (data: DeployComponentDto[]) => {
          this.data = this.createTreeFromList(
            data.map(node => this.convertDeployCompToTree(node)),
            undefined
          );
          const hegsTree = this.shadowRoot?.getElementById(
            'hegs-tree'
          ) as HegsTree;
          if (hegsTree) {
            hegsTree.componentsLoading = false;
          }
        },
        (err: any) => console.error(err),
        () => console.log('done loading project components')
      );
  }

  private createTreeFromList(
    list: TreeNode[],
    parent: TreeNode | undefined
  ): TreeNode[] {
    const output: TreeNode[] = [];

    const id = parent === undefined ? 0 : parent.id;

    const parents = list.filter(node => node.parentId === id);

    if (parents.length > 0) {
      output.push(...parents);
      output.forEach(root => {
        if (root.numOfChildren > 0)
          root.children = this.createTreeFromList(list, root);
      });
      return output;
    }
    return [];
  }

  private convertDeployCompToTree(deploy: DeployComponentDto): TreeNode {
    console.log(
      `Converting ${deploy.Name} with ${deploy.NumOfChildren} children to tree`
    );
    const numChildren = deploy.NumOfChildren ?? 0;
    const child: TreeNode = {
      id: deploy.Id ?? 0,
      icon: '',
      name: deploy.Name ?? '',
      open: false,
      children: [],
      numOfChildren: numChildren,
      hasParent: (numChildren ?? 0) > 0,
      parentId: deploy.ParentId ?? 0,
      checked: false,
      indeterminate: false
    };
    return child;
  }

  private AddOverrideProperty() {
    const find = this.properties?.find(
      value => value.Name === this.propertyName
    );

    if (find === undefined) {
      alert('Please select a property from the list!');
      return;
    }

    if (this.propertyValue === '') {
      alert('The property must contain a value!');
      return;
    }

    this.propertyOverrides.push({
      PropertyName: find.Name,
      PropertyValue: this.propertyValue
    });
    this.propertyOverrides = JSON.parse(JSON.stringify(this.propertyOverrides));
  }

  _propNameValueChanged(data: any) {
    if (data) {
      const combo = data.target as ComboBox;
      this.propertyName = combo.value;
    }
  }

  private _propValueChanged(data: any) {
    if (data) {
      const field = data.target as TextField;
      this.propertyValue = field.value as string;
    }
  }

  removeItem<T>(arr: Array<T>, value: T): Array<T> {
    const index = arr.indexOf(value);
    if (index > -1) {
      arr.splice(index, 1);
    }
    return arr;
  }

  private removePropertyOverride(propertyOverride: RequestProperty) {
    const splicedArray = this.removeItem(
      this.propertyOverrides,
      propertyOverride
    );

    this.propertyOverrides = JSON.parse(JSON.stringify(splicedArray));
  }

  private _buildValueChanged(data: Event) {
    const combo = data.target as ComboBox;
    if (combo) {
      this.selectedBuild = combo.value as string;

      const found = this.builds?.find(
        value => value.Name === this.selectedBuild
      )?.Id;

      this.selectedBuildId = found !== null ? found : undefined;
    }
  }

  private openDeployDialog() {
    this.checkDeployment(true);
  }

  private checkDeployment(alertUser: boolean) {
    const hegsTree = this.shadowRoot?.getElementById('hegs-tree') as HegsTree;

    if (this.project === null || this.project === undefined) {
      if (alertUser) alert('Please select a project!');
      return false;
    }
    let folder = this.project.ArtefactsUrl;
    if (this.project.ArtefactsUrl?.endsWith('/')) {
      folder = this.project.ArtefactsUrl?.substring(
        0,
        (this.project.ArtefactsUrl?.length ?? 0) - 1
      );
    }

    const checkedElems = hegsTree.getCheckedComponents();
    const components = checkedElems.map(e => e.data.name);

    this.req = { requestDto: {} };
    this.req = {
      requestDto: {
        Project: this.project.ProjectName,
        Environment: this.envName,
        BuildUrl: this.isFolderProject
          ? `${folder}/${this.selectedBuild}`
          : this.selectedBuildId,
        BuildText: this.buildDef,
        BuildNum: this.selectedBuild,
        RequestProperties: this.propertyOverrides,
        Components: components
      }
    };

    if (
      this.req.requestDto?.Project === '' ||
      this.req.requestDto?.Project === undefined
    ) {
      if (alertUser) alert('Please select a project!');
      return false;
    }

    if (
      this.req.requestDto?.Environment === '' ||
      this.req.requestDto?.Environment === undefined
    ) {
      if (alertUser) alert('Please select an environment!');
      return false;
    }

    if (
      this.req.requestDto?.BuildUrl === '' ||
      this.req.requestDto?.BuildUrl === undefined
    ) {
      if (alertUser) alert('Please select a build for deployment!');
      return false;
    }

    if (this.req.requestDto?.Components?.length === 0) {
      if (alertUser)
        alert('Please select at least one component for deployment!');
      return false;
    }

    this.dialog.deployJson = this.req;
    this.dialog.Open();
    return true;
  }

  startDeployment() {
    this.ErrorMessage = '';
    this.deploymentStarting = true;
    const api = new RequestApi();
    api.requestPost(this.req).subscribe({
      next: (data: RequestStatusDto) => {
        this.requestedDeployment = data;

        const not = new SuccessfulDeployNotification();
        not.setAttribute('envName', this.envName);
        not.setAttribute('selectedBuild', this.selectedBuild);
        not.setAttribute(
          'requestedDeploymentId',
          this.requestedDeployment?.Id?.toString() ?? ''
        );
        this.shadowRoot?.appendChild(not);
        not.open();
        this.lastDeploymentId = data.Id ?? 0;
        this.deploymentStarting = false;
      },
      error: (err: any) => {
        console.error(err.response);
        this.ErrorMessage = err.response;
        this.deploymentStarting = false;
      },
      complete: () => {
        const tree = this.shadowRoot?.getElementById('hegs-tree') as HegsTree;
        if (tree) {
          tree.ResetCheckedStates();
        }
        console.log('done starting new deployment request');
      }
    });
  }

  private loadBuildDefinitions() {
    if (
      this._project !== undefined &&
      this.lastProjectIdBuildDefs !== this._project
    ) {
      this.clearComboboxSelectedItem('build-defs');
      this.clearComboboxSelectedItem('builds');
      this.clearComboboxSelectedItem('folders');

      this.builds = [];
      this.buildDefinitions = [];
      this.buildDefsLoading = true;

      const reqApi = new RequestApi();
      reqApi
        .requestBuildDefinitionsGet({ projectId: this._project.ProjectId ?? 0 })
        .subscribe({
          next: (data: DeployArtefactDto[]) => {
            this.setBuildDefinitions(data);
            this.lastProjectIdBuildDefs = this._project?.ProjectId ?? 0;
          },
          error: (err: any) => {
            console.error(err);

            let error = '';
            if (err.response.ExceptionMessage !== undefined)
              error = err.response.ExceptionMessage;
            else error = err.response.Message;

            const notification = new ErrorNotification();
            notification.setAttribute('errorMessage', error);

            this.shadowRoot?.appendChild(notification);
            notification.open();
            this.buildDefsLoading = false;
            this.buildsLoading = false;
          },
          complete: () => console.log('done loading build definitions')
        });
      this.getProjectComponents();
    }
  }

  private clearComboboxSelectedItem(comboName: string) {
    const combo = this.shadowRoot?.getElementById(comboName) as ComboBox;
    if (combo) combo.selectedItem = undefined;
  }
}
