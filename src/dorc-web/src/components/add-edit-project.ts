import { css, LitElement } from 'lit';
import '@vaadin/text-field';
import '@vaadin/combo-box';
import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/checkbox';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/dialog';
import '@vaadin/vertical-layout';
import '@vaadin/horizontal-layout';
import '../components/hegs-dialog';
import { TextField } from '@vaadin/text-field';
import { HegsDialog } from './hegs-dialog';
import { RefDataProjectsApi } from '../apis/dorc-api';
import type { ProjectApiModel } from '../apis/dorc-api';


@customElement('add-edit-project')
export class AddEditProject extends LitElement {

  private readonly maxFieldLength = 512;

  @property({ type: Object })
  get project(): ProjectApiModel {
    return this._project;
  }

  set project(value: ProjectApiModel) {
    if (value === undefined) return;
    const oldVal = this._project;
    this._project = JSON.parse(JSON.stringify(value));

    this.setTextField('proj-name', this._project.ProjectName ?? '');
    this.setTextField('proj-desc', this._project.ProjectDescription ?? '');
    this.setTextField('proj-url', this._project.ArtefactsUrl ?? '');
    this.setTextField('proj-azure', this._project.ArtefactsSubPaths ?? '');
    this.setTextField('proj-regex', this._project.ArtefactsBuildRegex ?? '');
    this.setTextField('proj-terraform-git-url', this._project.TerraformGitRepoUrl ?? '');

    // Reset so updated() will set the combo-box for the new project
    this._comboBoxInitialized = false;
    this.requestUpdate('project', oldVal);
  }

  private get isGitHub(): boolean {
    return String(this._project?.SourceControlType) === 'GitHub';
  }

  private get isFileShare(): boolean {
    return String(this._project?.SourceControlType) === 'FileShare';
  }

  private get urlLabel(): string {
    if (this.isFileShare) return 'File Share Path (e.g., file://server/share/builds)';
    if (this.isGitHub) return 'GitHub API URL (e.g., https://api.github.com/repos/owner/repo)';
    return 'Azure DevOps Server URL';
  }

  private get subPathsLabel(): string {
    if (this.isFileShare) return 'Sub-paths (semicolon-separated)';
    if (this.isGitHub) return 'GitHub Workflow Files (semicolon-separated, e.g., build.yml;deploy.yml)';
    return 'Azure DevOps Server Project';
  }

  private get buildRegexLabel(): string {
    if (this.isFileShare) return 'Build Filter Regex';
    if (this.isGitHub) return 'Workflow Name Regex';
    return 'Build Definition Regex';
  }

  private get showSubPaths(): boolean {
    return !this.isFileShare;
  }

  private get showBuildRegex(): boolean {
    return !this.isFileShare;
  }

  @property({ type: Array })
  get projects(): ProjectApiModel[] {
    return this._projects;
  }

  set projects(value: ProjectApiModel[]) {
    if (value === undefined) return;
    const oldVal = this._projects;
    this._projects = value;
    if (this._projects !== undefined)
      this.allProjNames = this._projects.map(proj => proj.ProjectName ?? '');
    this.requestUpdate('projects', oldVal);
  }

  setTextField(id: string, value: string) {
    const textField = this.shadowRoot?.getElementById(id) as TextField;
    if (textField) textField.value = value;
  }

  private _comboBoxInitialized = false;

  protected updated(_changedProperties: Map<string, unknown>) {
    super.updated(_changedProperties);
    // Set combo-box value once after it first appears in DOM
    if (!this._comboBoxInitialized) {
      const comboBox = this.shadowRoot?.getElementById('proj-source-control') as any;
      if (comboBox) {
        comboBox.value = String(this._project?.SourceControlType ?? 'AzureDevOps');
        this._comboBoxInitialized = true;
      }
    }
  }

  private _project = this.getEmptyProj();

  private _projects: ProjectApiModel[] = [];

  @property({ type: Boolean })
  canSubmit = false;

  private projValid = false;

  private isNameValid = false;

  private isBusy = false;

  @property() ErrorMessage = '';

  private allProjNames: string[] | undefined;

  @query('#dialog') dialog!: HegsDialog;

  static get styles() {
    return css`
      vaadin-text-field,
      vaadin-combo-box {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 490px;
        padding: 5px;
      }
      .tooltip {
        position: relative;
        display: inline-block;
      }
      .tooltip .tooltiptext {
        visibility: hidden;
        width: 300px;
        background-color: black;
        color: #fff;
        text-align: center;
        border-radius: 6px;
        padding: 5px 0;

        /* Position the tooltip */
        position: absolute;
        z-index: 1;
      }
      .tooltip:hover .tooltiptext {
        visibility: visible;
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

  render() {
    return html`
      <hegs-dialog
        id="dialog"
        @dialog-close="${this.close}"
        title="Edit Project Metadata"
      >
        <vaadin-vertical-layout>
          <vaadin-text-field
            id="proj-name"
            style="width: 490px;"
            label="Name"
            required
            min-length="5"
            value="${this._project?.ProjectName ?? ''}"
            @value-changed="${this._projNameValueChanged}"
          ></vaadin-text-field>
          <vaadin-text-field
            id="proj-desc"
            style="width: 490px;"
            label="Description"
            value="${this._project?.ProjectDescription ?? ''}"
            @value-changed="${this._descriptionChanged}"
          ></vaadin-text-field>
          <vaadin-combo-box
            id="proj-source-control"
            style="width: 490px;"
            label="Source Control Type"
            .items="${[
              { label: 'Azure DevOps', value: 'AzureDevOps' },
              { label: 'GitHub', value: 'GitHub' },
              { label: 'File Share', value: 'FileShare' }
            ]}"
            item-label-path="label"
            item-value-path="value"
            @value-changed="${this._sourceControlTypeChanged}"
          ></vaadin-combo-box>
          <vaadin-text-field
            id="proj-url"
            style="width: 490px;"
            label="${this.urlLabel}"
            maxlength="${this.maxFieldLength}"
            title="Maximum length: ${this.maxFieldLength} symbols"
            required
            pattern="^((https?|file):\\/\\/(.*)|\\\\\\\\(.*))"
            value="${this._project?.ArtefactsUrl ?? ''}"
            @value-changed="${this._artefactsUrlChanged}"
          ></vaadin-text-field>
          ${this.showSubPaths ? html`
          <vaadin-text-field
            id="proj-azure"
            style="width: 490px;"
            label="${this.subPathsLabel}"
            maxlength="${this.maxFieldLength}"
            title="Maximum length: ${this.maxFieldLength} symbols"
            required
            min-length="6"
            value="${this._project?.ArtefactsSubPaths ?? ''}"
            @value-changed="${this._artefactsSubPathsChanged}"
          ></vaadin-text-field>` : html``}
          ${this.showBuildRegex ? html`
          <vaadin-text-field
            id="proj-regex"
            style="width: 490px;"
            label="${this.buildRegexLabel}"
            value="${this._project?.ArtefactsBuildRegex ?? ''}"
            @value-changed="${this._buildDefinitionRegexChanged}"
          ></vaadin-text-field>` : html``}
          <vaadin-text-field
            id="proj-terraform-git-url"
            style="width: 490px;"
            label="Terraform Git Repository URL"
            maxlength="${this.maxFieldLength}"
            title="Maximum length: ${this.maxFieldLength} symbols"
            value="${this._project?.TerraformGitRepoUrl ?? ''}"
            @value-changed="${this._terraformGitRepoUrlChanged}"
            helper-text="Git repository URL for Terraform code (e.g., https://github.com/org/repo.git)"
          ></vaadin-text-field>
          <div style="color: var(--dorc-error-color)">${this.ErrorMessage}</div>
          <vaadin-horizontal-layout style="margin-right: 30px">
            <vaadin-button
              style="margin: 2px"
              .disabled="${!this.canSubmit}"
              @click="${this._submit}"
              >Save
            </vaadin-button>
            <vaadin-button @click="${this.Reset}" style="margin: 2px"
              >Clear
            </vaadin-button>
          </vaadin-horizontal-layout>
        </vaadin-vertical-layout>
      </hegs-dialog>
    `;
  }

  getEmptyProj(): ProjectApiModel {
    return {
      ProjectDescription: '',
      ProjectId: 0,
      ProjectName: '',
      ArtefactsBuildRegex: '',
      ArtefactsSubPaths: '',
      ArtefactsUrl: '',
      SourceControlType: 'AzureDevOps' as any
    };
  }

  public open() {
    this.dialog.open = true;
    this.ErrorMessage = '';
  }

  public close() {
    this.ErrorMessage = '';
  }

  _projNameValueChanged(data: any) {
    this.checkProjectNameValid(data);
  }

  private checkProjectNameValid(data: any) {
    if (this._project !== undefined && data.target !== undefined) {
      this._project.ProjectName = data.target.value as string;
      this._project.ProjectName = this._project.ProjectName.trim();

      this._checkName(this._project.ProjectName);
    }
  }

  _buildDefinitionRegexChanged(data: any) {
    if (this._project !== undefined && data.target !== undefined) {
      const model: ProjectApiModel = JSON.parse(JSON.stringify(this._project));

      model.ArtefactsBuildRegex = data.target.value;
      this._project = model;
      this._inputValueChanged();
    }
  }

  _descriptionChanged(data: any) {
    if (this._project !== undefined && data.target !== undefined) {
      const model: ProjectApiModel = JSON.parse(JSON.stringify(this._project));

      model.ProjectDescription = data.target.value;
      this._project = model;
      this._inputValueChanged();
    }
  }

  _artefactsUrlChanged(data: any) {
    if (this._project !== undefined && data.target !== undefined) {
      const model: ProjectApiModel = JSON.parse(JSON.stringify(this._project));

      model.ArtefactsUrl = data.target.value;
      this._project = model;
      this._validateUrlForProvider();
      this._inputValueChanged();
    }
  }

  _artefactsSubPathsChanged(data: any) {
    if (this._project !== undefined && data.target !== undefined) {
      const model: ProjectApiModel = JSON.parse(JSON.stringify(this._project));

      model.ArtefactsSubPaths = data.target.value;
      this._project = model;
      this._inputValueChanged();
    }
  }

  _terraformGitRepoUrlChanged(data: any) {
    if (this._project !== undefined && data.target !== undefined) {
      const model: ProjectApiModel = JSON.parse(JSON.stringify(this._project));

      model.TerraformGitRepoUrl = data.target.value;
      this._project = model;
      this._inputValueChanged();
    }
  }

  _sourceControlTypeChanged(data: any) {
    if (this._project !== undefined && data.target !== undefined) {
      const newValue = data.target.value;
      if (newValue === String(this._project.SourceControlType)) return;
      const model: ProjectApiModel = JSON.parse(JSON.stringify(this._project));
      model.SourceControlType = newValue as any;
      this._project = model;
      this._validateUrlForProvider();
      this.requestUpdate();
    }
  }

  private _validateUrlForProvider() {
    const urlField = this.shadowRoot?.getElementById('proj-url') as TextField;
    if (!urlField) return;

    const url = (this._project?.ArtefactsUrl ?? '').trim();
    const provider = String(this._project?.SourceControlType ?? 'AzureDevOps');

    if (!url) {
      urlField.errorMessage = '';
      urlField.invalid = false;
      return;
    }

    const isFileUrl = url.toLowerCase().startsWith('file:') || url.startsWith('\\\\');

    let hostname = '';
    if (!isFileUrl) {
      try {
        hostname = new URL(url).hostname.toLowerCase();
      } catch {
        // Not a valid URL yet — skip validation until it is
        urlField.errorMessage = '';
        urlField.invalid = false;
        return;
      }
    }

    const isGitHubHost = hostname === 'github.com' || hostname.endsWith('.github.com');
    const isDevOpsHost = hostname === 'dev.azure.com' || hostname.endsWith('.dev.azure.com') ||
      hostname === 'visualstudio.com' || hostname.endsWith('.visualstudio.com') ||
      // TFS-style on-prem hosts typically look like `tfs.<corp-domain>` or
      // `tfs<N>.<corp-domain>`; `.endsWith('.tfs.')` can never match because
      // a hostname never ends with a trailing dot. Use a substring / prefix
      // check so common on-prem conventions are recognised.
      hostname === 'tfs' || hostname.startsWith('tfs.') || hostname.includes('.tfs.');

    let error = '';

    if (provider === 'GitHub' && !isGitHubHost && !isFileUrl) {
      if (isDevOpsHost) {
        error = 'This looks like an Azure DevOps URL. Change the provider or use a GitHub API URL.';
      }
    } else if (provider === 'AzureDevOps' && isGitHubHost) {
      error = 'This looks like a GitHub URL. Change the provider to GitHub or use an Azure DevOps URL.';
    } else if (provider === 'FileShare' && !isFileUrl) {
      if (isGitHubHost || isDevOpsHost) {
        error = 'FileShare projects require a file:// path, not a web URL.';
      }
    }

    urlField.errorMessage = error;
    urlField.invalid = !!error;
    this._canSubmit();
  }

  _checkName(data: string) {
    const found = this.allProjNames?.find(name => name === data);
    // New environment check
    if (found === undefined && data.length > 0) {
      this.isNameValid = true;
    }
    // Existing environment check
    else if (found !== undefined && data === found) {
      this.isNameValid = true;
    } else {
      this.isNameValid = false;
    }

    this._canSubmit();
  }

  _inputValueChanged() {
    let result = true;
    if (this._project !== undefined) {
      if (
        this._project.ProjectName !== undefined &&
        (this._project.ProjectName?.length ?? 0) < 1
      ) {
        result = false;
      }
      if (
        this._project.ArtefactsUrl !== undefined &&
        (this._project.ArtefactsUrl?.length ?? 0) < 1
      ) {
        result = false;
      }
      if (
        this._project.ArtefactsSubPaths !== undefined &&
        (this._project.ArtefactsSubPaths?.length ?? 0) < 1
      ) {
        result = false;
      }
      this.projValid = result;
      this._canSubmit();
    }
  }

  _canSubmit() {
    const urlField = this.shadowRoot?.getElementById('proj-url') as TextField;
    const urlValid = !urlField?.invalid;
    this.canSubmit = this.projValid && this.isNameValid && urlValid && !this.isBusy;
  }

  _submit() {
    this._setBusy();
    if (this._project.ProjectId === 0) {
      const api = new RefDataProjectsApi();
      api.refDataProjectsPost({ projectApiModel: this._project }).subscribe({
        next: () => {
          this.projAdded();
        },
        error: (err: any) => {
          this._setUnbusy();
          console.error(err.response);
          this.errorAlert(err);
        },
        complete: () => {
          this._setUnbusy();
          console.log('done adding project');
        }
      });
    } else {
      const api = new RefDataProjectsApi();
      api.refDataProjectsPut({ projectApiModel: this._project }).subscribe({
        next: (data: ProjectApiModel) => {
          if (data !== null) {
            this.projUpdated(data);
          }
        },
        error: (err: any) => {
          this._setUnbusy();
          console.error(err.response);
          this.errorAlert(err);
        },
        complete: () => {
          this._setUnbusy();
          console.log('done updating project');
        }
      });
    }
  }

  _setBusy() {
    this.isBusy = true;
    this._canSubmit();
  }

  _setUnbusy() {
    this.isBusy = false;
    this._canSubmit();
  }

  errorAlert(event: any) {
    let msg = '';
    if (event.response?.ExceptionMessage) msg = event.response.ExceptionMessage;
    else if (event.response?.Message) msg = event.response.Message;
    else if (event.response) msg = event.response;

    this.ErrorMessage = msg;
  }

  projUpdated(data: ProjectApiModel) {
    const event = new CustomEvent('project-updated', {
      detail: {
        project: data
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
    this.dialog.close();
    this.Reset();
  }

  projAdded() {
    const event = new CustomEvent('project-added', {
      detail: {
        project: this._project
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
    this.dialog.close();
    this.Reset();
  }

  Reset() {
    this.project = this.getEmptyProj();
    this.ErrorMessage = '';
  }
}
