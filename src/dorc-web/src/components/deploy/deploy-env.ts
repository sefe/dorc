import { columnBodyRenderer } from '@vaadin/grid/lit';
import { comboBoxRenderer } from '@vaadin/combo-box/lit';
import '@vaadin/button';
import '@vaadin/checkbox';
import '@vaadin/combo-box';
import { ComboBox } from '@vaadin/combo-box';
import '@vaadin/details';
import '@vaadin/dialog';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/horizontal-layout';
import '@vaadin/notification';
import '@vaadin/text-field';
import { TextField } from '@vaadin/text-field';
import { css, LitElement, PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
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
import type { ProjectApiModel, RequestDto } from '../../apis/dorc-api';
import '@vaadin/confirm-dialog';
import { Notification } from '@vaadin/notification';
import '../hegs-json-viewer';

/** Extends the auto-generated RequestDto with CR fields until the next swagger regen */
interface RequestDtoWithCr extends RequestDto {
  ChangeRequestNumber?: string;
  OverrideCr?: boolean;
}
import type { ChangeRequestValidationResult } from '../../types/ChangeRequestTypes';
import { appConfig } from '../../app-config';
import {
  oauthServiceContainer,
  OAUTH_SCHEME
} from '../../services/Account/OAuthService';
import './property-override-controls';
import { ErrorNotification } from '../notifications/error-notification';
import './component-tree/hegs-tree';
import { HegsTree } from './component-tree/hegs-tree';
import { TreeNode } from './component-tree/TreeNode';
import { SuccessfulDeployNotification } from './notifications/successful-deploy-notification';
import { HegsJsonViewer } from '../hegs-json-viewer';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';

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

  private get isGitHubProject(): boolean {
    return String(this._project?.SourceControlType) === 'GitHub';
  }

  @property({ type: Array }) buildDefinitions: DeployArtefactDto[] = [];

  @property({ type: Array }) builds: DeployArtefactDto[] = [];

  @property({ type: String }) envName = '';

  @property({ type: Boolean }) envIsProd = false;

  @property({ type: Array }) data: TreeNode[];

  @property({ type: Array }) propertyOverrides: RequestProperty[] = [];

  @property({ type: Array }) properties: PropertyApiModel[] | undefined;

  @property({ type: Boolean }) buildDefsLoading = false;

  @property({ type: Boolean }) buildsLoading = false;

  @property({ type: Boolean }) isFolderProject = false;

  @property({ type: String }) selectedBuildId: string | undefined;

  @property({ type: Number }) lastDeploymentId = 0;

  @property({ type: Boolean }) deploymentStarting = false;

  @property() ErrorMessage = '';

  @state() private crNumber = '';
  @state() private crValidationResult: ChangeRequestValidationResult | null =
    null;
  @state() private crValidating = false;
  @state() private crCreating = false;
  @state() private overrideCr = false;

  @property({ type: Object }) req!: RequestPostRequest;

  @state() private overrideConfirmOpened = false;
  @state()
  dialogOpened = false;

  @state() private requestedDeployment: RequestStatusDto | undefined;

  static get styles() {
    return css`
      :host {
        overflow-y: scroll;
      }
      [hidden] {
        display: none !important;
      }
      .build-defs-section {
        display: flex;
        flex-direction: column;
        gap: var(--lumo-space-xs);
        width: 100%;
        max-width: 600px;
        margin-left: 10px;
      }
      .combo-row {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-s);
      }
      .folder-artifacts-section {
        display: flex;
        align-items: center;
        gap: var(--lumo-space-s);
        width: 100%;
        max-width: 600px;
        margin-left: 10px;
      }
      vaadin-combo-box {
        padding-top: 0px;
      }
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(30vh - 110px);
        min-height: 150px;
        --divider-color: var(--dorc-border-color);
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
      }
      .cr-section {
        margin: 12px 12px 0 12px;
        padding: 12px;
        border-top: 6px solid var(--dorc-link-color);
        background-color: var(--dorc-bg-secondary);
      }
      .cr-validation-success {
        background: var(--dorc-success-bg);
        border: 1px solid #4caf50;
        border-radius: 4px;
        padding: 12px;
        margin-top: 8px;
      }
      .cr-validation-error {
        background: var(--dorc-failure-bg);
        border: 1px solid var(--dorc-error-color);
        border-radius: 4px;
        padding: 12px;
        margin-top: 8px;
      }
      .cr-details-grid {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: 4px 12px;
        margin-top: 8px;
      }
      .cr-details-grid dt {
        font-weight: 600;
        color: var(--dorc-text-secondary);
        margin: 0;
      }
      .cr-details-grid dd {
        margin: 0;
        color: var(--dorc-text-primary);
      }
      .cr-override-row {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-top: 8px;
      }
      .cr-progress-bar {
        width: 100%;
        height: 4px;
        background: var(--dorc-bg-tertiary);
        border-radius: 2px;
        overflow: hidden;
        margin-top: 8px;
      }
      .cr-progress-bar-inner {
        height: 100%;
        width: 40%;
        background: linear-gradient(90deg, var(--dorc-link-color), #42a5f5);
        border-radius: 2px;
        animation: cr-progress-slide 1.2s ease-in-out infinite;
      }
      @keyframes cr-progress-slide {
        0% {
          transform: translateX(-100%);
        }
        100% {
          transform: translateX(350%);
        }
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

    const api = new PropertiesApi(dorcApiConfiguration);
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
      <vaadin-confirm-dialog
        id="dialog"
        theme="deploy-preview"
        header="New deployment"
        confirm-text="Deploy"
        cancel-theme="primary"
        cancel-button-visible
        .opened="${this.dialogOpened}"
        @opened-changed="${(e: CustomEvent) => {
          this.dialogOpened = (e.detail as { value: boolean }).value;
        }}"
        @confirm="${this.startDeployment}"
      >
        <div style="margin-bottom: 5px;">
          Please confirm you want to submit this deployment request?
        </div>
        <hegs-json-viewer id="jsonviewer">{}</hegs-json-viewer>
      </vaadin-confirm-dialog>
      <vaadin-confirm-dialog
        id="override-confirm"
        header="Override Change Request"
        confirm-text="Override"
        cancel-text="Cancel"
        cancel-button-visible
        confirm-theme="error primary"
        .opened="${this.overrideConfirmOpened}"
        @opened-changed="${(e: CustomEvent) => {
          this.overrideConfirmOpened = (e.detail as { value: boolean }).value;
        }}"
        @confirm="${this._onOverrideConfirmed}"
        @cancel="${this._onOverrideCancelled}"
      >
        You are about to deploy to production WITHOUT a valid Change Request. App Support will be notified by email. Are you sure you want to proceed?
      </vaadin-confirm-dialog>
      >
        <div style="margin-bottom: 5px;">
          Please confirm you want to submit this deployment request?
        </div>
        <hegs-json-viewer id="jsonviewer">{}</hegs-json-viewer>
      </vaadin-confirm-dialog>
      <div class="build-defs-section" ?hidden="${this.isFolderProject}">
        <div class="combo-row">
          <vaadin-combo-box
            id="build-defs"
            style="flex: 1;"
            @value-changed="${this._buildDefValueChanged}"
            .items="${this.buildDefinitions}"
            ${comboBoxRenderer(this._buildRenderer, [])}
            placeholder="${this.isGitHubProject ? 'Select Workflow' : 'Select Build Definition'}"
            label="${this.isGitHubProject ? 'Workflow' : 'Build Definition'}"
            clear-button-visible
            item-label-path="Name"
            item-value-path="Name"
          ></vaadin-combo-box>
          ${
            this.buildDefsLoading
              ? html` <div class="small-loader"></div> `
              : html``
          }
        </div>
        <div class="combo-row">
          <vaadin-combo-box
            id="builds"
            style="flex: 1;"
            @value-changed="${this._buildValueChanged}"
            .items="${this.builds}"
            ${comboBoxRenderer(this._buildRenderer, [])}
            placeholder="${this.isGitHubProject ? 'Select Workflow Run' : 'Select Build Number'}"
            label="${this.isGitHubProject ? 'Workflow Run' : 'Build Number'}"
            clear-button-visible
            item-label-path="Name"
            item-value-path="Name"
          ></vaadin-combo-box>
          ${
            this.buildsLoading
              ? html` <div class="small-loader"></div> `
              : html``
          }
        </div>
      </div>
      <div class="folder-artifacts-section" ?hidden="${!this.isFolderProject}">
        <vaadin-combo-box
          id="folders"
          style="flex: 1;"
          @value-changed="${this._buildValueChanged}"
          .items="${this.builds}"
          ${comboBoxRenderer(this._buildRenderer, [])}
          placeholder="Select Folder"
          label="Folder Artifacts"
          clear-button-visible
          item-label-path="Name"
          item-value-path="Name"
        ></vaadin-combo-box>
        ${
          this.buildsLoading ? html` <div class="small-loader"></div> ` : html``
        }
      </div>
      <vaadin-details
        opened
        summary="Components"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; padding-left: 10px"
      >
        <hegs-tree id="hegs-tree" .data="${this.data}"></hegs-tree>
      </vaadin-details>
      <vaadin-details
        closed
        summary="Property Overrides (Optional)"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; padding-left: 10px"
      >
        <vaadin-vertical-layout style="align-items: stretch">
          <vaadin-combo-box
            @value-changed="${this._propNameValueChanged}"
            .items="${this.properties}"
            placeholder="Select Property"
            clear-button-visible
            item-label-path="Name"
            item-value-path="Name"
            style="width: 100%; max-width: 600px"
          ></vaadin-combo-box>
          <vaadin-text-field
            required
            placeholder="Property Value"
            @value-changed="${this._propValueChanged}"
            style="width: 100%; max-width: 500px"
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
              ${columnBodyRenderer(this._boundPropOverridesButtonsRenderer, [])}
              resizable
            ></vaadin-grid-column>
          </vaadin-grid>
        </vaadin-vertical-layout>
      </vaadin-details>
      ${this.envIsProd ? this._renderCrSection() : html``}
      <vaadin-button
        style="width: 100%; max-width: 600px; margin-left: var(--lumo-space-s); margin-bottom: var(--lumo-space-xl)"
        @click="${this.openDeployDialog}"
        theme="primary"
        >Deploy
      </vaadin-button>
      ${this.deploymentStarting ? html` <div class="loader"></div> ` : html``}
      <div style="color: var(--dorc-error-color)">${this.ErrorMessage}</div>
    `;
  }

  private _renderCrSection() {
    return html`
      <div class="cr-section">
        <strong>Production Deployment — Change Request</strong>
        <div
          style="display: flex; align-items: flex-end; gap: 8px; margin-top: 8px;"
        >
          <vaadin-text-field
            label="CR Number"
            placeholder="e.g. CHG0012345"
            style="width: 300px"
            .value="${this.crNumber}"
            @value-changed="${(e: CustomEvent) => {
              this.crNumber = (e.target as HTMLInputElement).value;
            }}"
          ></vaadin-text-field>
          <vaadin-button
            theme="primary"
            ?disabled="${this.crValidating || !this.crNumber}"
            @click="${this._validateCr}"
          >
            ${this.crValidating ? html`Validating...` : html`Validate`}
          </vaadin-button>
          <vaadin-button
            theme="secondary"
            ?disabled="${this.crCreating || !!this.crNumber}"
            @click="${this._autoCreateCr}"
            title="Automatically create a standard Change Request in ServiceNow"
          >
            ${this.crCreating ? html`Creating...` : html`Auto-create CR`}
          </vaadin-button>
        </div>
        ${
          this.crCreating
            ? html`
                <div class="cr-progress-bar">
                  <div class="cr-progress-bar-inner"></div>
                </div>
                <div
                  style="color: var(--dorc-text-secondary); font-size: 13px; margin-top: 4px;"
                >
                  Creating Change Request in ServiceNow...
                </div>
              `
            : html``
        }
        ${this.crValidationResult ? this._renderCrResult() : html``}
        ${
          !this.crValidationResult || !this.crValidationResult.IsValid
            ? html`
                <div class="cr-override-row">
                  <vaadin-checkbox
                    id="override-cr-checkbox"
                    .checked="${this.overrideCr}"
                    @checked-changed="${this._handleOverrideChange}"
                  ></vaadin-checkbox>
                  <span
                    style="color: var(--dorc-error-color); font-weight: 500;"
                  >
                    ⚠ Override CR — App Support will be notified by email
                  </span>
                </div>
              `
            : html``
        }
      </div>
    `;
  }

  private _renderCrResult() {
    const r = this.crValidationResult!;
    const cssClass = r.IsValid
      ? 'cr-validation-success'
      : 'cr-validation-error';
    const icon = r.IsValid ? '✓' : '✗';
    return html`
      <div class="${cssClass}">
        <strong>${icon} ${r.Message}</strong>
        <dl class="cr-details-grid">
          ${
            r.ShortDescription
              ? html`<dt>Description</dt>
                  <dd>${r.ShortDescription}</dd>`
              : html``
          }
          ${
            r.State
              ? html`<dt>State</dt>
                  <dd>${r.State}</dd>`
              : html``
          }
          ${
            r.StartDate || r.EndDate
              ? html`<dt>Change Window</dt>
                  <dd>${r.StartDate ?? 'N/A'} — ${r.EndDate ?? 'N/A'}</dd>`
              : html``
          }
        </dl>
      </div>
    `;
  }

  private _getAuthHeaders(contentType?: string): Record<string, string> {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (contentType) headers['Content-Type'] = contentType;
    if (appConfig.authenticationScheme === OAUTH_SCHEME) {
      const token = oauthServiceContainer.service.signedInUser?.access_token;
      if (token) headers['Authorization'] = `Bearer ${token}`;
    }
    return headers;
  }

  private async _extractErrorMessage(
    response: Response,
    fallback: string
  ): Promise<string> {
    const text = await response.text();
    if (!text) return fallback;
    try {
      const body = JSON.parse(text);
      return body.Message || body.message || text;
    } catch {
      return text;
    }
  }

  private _crError(message: string): ChangeRequestValidationResult {
    return { IsValid: false, Message: message };
  }

  private async _validateCr() {
    if (!this.crNumber) return;
    this.crValidating = true;
    this.crValidationResult = null;

    try {
      const response = await fetch(
        `${appConfig.dorcApi}/api/ChangeRequest/validate?crNumber=${encodeURIComponent(this.crNumber)}`,
        { headers: this._getAuthHeaders(), credentials: 'include' }
      );
      if (!response.ok) {
        this.crValidationResult = this._crError(
          await this._extractErrorMessage(
            response,
            `Validation failed (HTTP ${response.status})`
          )
        );
        return;
      }
      const result: ChangeRequestValidationResult = await response.json();
      this.crValidationResult = result;
      if (result.IsValid) this.overrideCr = false;
    } catch (err) {
      this.crValidationResult = this._crError(
        `Failed to validate Change Request: ${err instanceof Error ? err.message : String(err)}`
      );
    } finally {
      this.crValidating = false;
    }
  }

  private async _autoCreateCr() {
    this.crCreating = true;
    this.crValidationResult = null;

    try {
      const response = await fetch(
        `${appConfig.dorcApi}/api/ChangeRequest/create`,
        {
          method: 'POST',
          headers: this._getAuthHeaders('application/json'),
          credentials: 'include',
          body: JSON.stringify({
            ProjectName: this.project?.ProjectName ?? '',
            Environment: this.envName,
            BuildNumber: this.selectedBuild || this.buildDef || '',
            ShortDescription: '',
            RequestedBy: ''
          })
        }
      );

      if (!response.ok) {
        this.crValidationResult = this._crError(
          await this._extractErrorMessage(
            response,
            `Auto-create failed (HTTP ${response.status})`
          )
        );
        return;
      }

      const result = await response.json();
      if (!result?.Success) {
        this.crValidationResult = this._crError(
          result?.Message || 'Auto-create failed'
        );
        return;
      }

      // CR created — set the number so user can click Validate
      this.crNumber = result.CrNumber;
    } catch (err) {
      this.crValidationResult = this._crError(
        `Failed to auto-create Change Request: ${err instanceof Error ? err.message : String(err)}`
      );
    } finally {
      this.crCreating = false;
    }
  }

  private _handleOverrideChange(e: CustomEvent) {
    const isChecked = e.detail.value as boolean;
    if (isChecked && !this.overrideCr) {
      // User is trying to check the box — show confirmation dialog first
      // Revert the checkbox until confirmed
      const checkbox = e.target as HTMLInputElement;
      checkbox.checked = false;
      this.overrideConfirmOpened = true;
    } else if (!isChecked && this.overrideCr) {
      // User is unchecking — allow it directly
      this.overrideCr = false;
    }
  }

  private _onOverrideConfirmed() {
    this.overrideCr = true;
    this.overrideConfirmOpened = false;
  }

  private _onOverrideCancelled() {
    this.overrideCr = false;
    this.overrideConfirmOpened = false;
  }

  private _showAlert(message: string) {
    Notification.show(message, {
      position: 'middle',
      duration: 5000,
      theme: 'error'
    });
  }

  _boundPropOverridesButtonsRenderer(item: RequestProperty) {
    const propertyOverride = item as RequestProperty;

    return html`<property-override-controls
      .propertyOverride="${propertyOverride}"
      @property-override-removed="${(e: CustomEvent) => {
        this.removePropertyOverride(e.detail.propertyOverride);
      }}"
    ></property-override-controls>`;
  }

  _buildRenderer(item: DeployArtefactDto) {
    const template = item as DeployArtefactDto;

    return html`
      <vaadin-horizontal-layout>
        ${template.Name?.replace('[PINNED]', '')}
        ${
          template.Name?.includes('[PINNED]')
            ? html`<vaadin-icon icon="vaadin:pin"></vaadin-icon>`
            : html``
        }
      </vaadin-horizontal-layout>
    `;
  }

  setBuildDefinitions(projects: DeployArtefactDto[]) {
    const sortedBuildDefinitions = projects.sort(this.sortBuildDefinitions);
    this.buildDefinitions = sortedBuildDefinitions;
    const firstBuildDefinition = this.buildDefinitions[0];
    if (
      firstBuildDefinition &&
      (firstBuildDefinition.Name === 'Not a CI/CD Server Project' ||
        firstBuildDefinition.Name === 'Not an Azure DevOps Server Project')
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
    this.crNumber = '';
    this.crValidationResult = null;
    this.crValidating = false;
    this.overrideCr = false;
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
    const api = new RequestApi(dorcApiConfiguration);
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
          const message = err.response.Message ?? err.response.ExceptionMessage;
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
    const reqApi = new RequestApi(dorcApiConfiguration);
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
      this._showAlert('Please select a property from the list!');
      return;
    }

    if (this.propertyValue === '') {
      this._showAlert('The property must contain a value!');
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
      if (alertUser) this._showAlert('Please select a project!');
      return false;
    }
    let folder = this.project.ArtefactsUrl;
    if (this.project.ArtefactsUrl?.endsWith('/')) {
      folder = this.project.ArtefactsUrl?.substring(
        0,
        (this.project.ArtefactsUrl?.length ?? 0) - 1
      );
    }

    const components = hegsTree.getCheckedComponentNames();

    this.req = { requestDto: {} };
    const requestBody: RequestDtoWithCr = {
      Project: this.project.ProjectName,
      Environment: this.envName,
      BuildUrl: this.isFolderProject
        ? `${folder}/${this.selectedBuild}`
        : this.selectedBuildId,
      BuildText: this.buildDef,
      BuildNum: this.selectedBuild,
      RequestProperties: this.propertyOverrides,
      Components: components,
      ChangeRequestNumber: this.crNumber || undefined,
      OverrideCr: this.overrideCr || undefined
    };
    this.req = { requestDto: requestBody };

    if (
      this.req.requestDto?.Project === '' ||
      this.req.requestDto?.Project === undefined
    ) {
      if (alertUser) this._showAlert('Please select a project!');
      return false;
    }

    if (
      this.req.requestDto?.Environment === '' ||
      this.req.requestDto?.Environment === undefined
    ) {
      if (alertUser) this._showAlert('Please select an environment!');
      return false;
    }

    if (
      this.req.requestDto?.BuildUrl === '' ||
      this.req.requestDto?.BuildUrl === undefined
    ) {
      if (alertUser) this._showAlert('Please select a build for deployment!');
      return false;
    }

    if (this.req.requestDto?.Components?.length === 0) {
      if (alertUser)
        this._showAlert('Please select at least one component for deployment!');
      return false;
    }

    // For production environments, require either a validated CR or override
    if (this.envIsProd) {
      const hasCr = this.crNumber && this.crValidationResult?.IsValid;
      if (!hasCr && !this.overrideCr) {
        if (alertUser)
          this._showAlert(
            'A validated Change Request number is required for production deployments. ' +
              'Either enter and validate a CR number, or check the Override CR checkbox.'
          );
        return false;
      }
    }

    const jsonViewer = this.shadowRoot?.getElementById(
      'jsonviewer'
    ) as HegsJsonViewer | null;
    if (jsonViewer) {
      Object.assign(jsonViewer.data, this.req.requestDto);
      jsonViewer.expand('**');
    }
    this.dialogOpened = true;
    return true;
  }

  startDeployment() {
    this.ErrorMessage = '';
    this.deploymentStarting = true;
    const api = new RequestApi(dorcApiConfiguration);
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

      const reqApi = new RequestApi(dorcApiConfiguration);
      reqApi
        .requestBuildDefinitionsGet({ projectId: this._project.ProjectId ?? 0 })
        .subscribe({
          next: (data: DeployArtefactDto[]) => {
            this.setBuildDefinitions(data);
            this.lastProjectIdBuildDefs = this._project?.ProjectId ?? 0;
          },
          error: (err: any) => {
            console.error(err);

            const message =
              err.response?.ExceptionMessage ??
              err.response?.Message ??
              (typeof err.response === 'string'
                ? err.response
                : 'An unexpected error occurred');

            const notification = new ErrorNotification();
            notification.setAttribute('errorMessage', message);

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
