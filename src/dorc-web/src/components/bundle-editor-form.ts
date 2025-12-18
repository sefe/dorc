import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import '@vaadin/text-field';
import '@vaadin/text-area';
import '@vaadin/number-field';
import '@vaadin/combo-box';
import '@vaadin/button';
import '@vaadin/vertical-layout';
import '@vaadin/horizontal-layout';
import {
  BundledRequestsApiModel,
  
  BundledRequestsApi,
  ProjectApiModel
} from '../apis/dorc-api';
import { BundleEditorDialog } from './bundle-editor-dialog';
import * as ace from 'ace-builds';
import { ComboBox } from '@vaadin/combo-box';

@customElement('bundle-editor-form')
export class BundleEditorForm extends LitElement {
  static styles = css`
    :host {
      display: block;
      padding: 1rem;
    }

    .field-container {
      width: 100%;
    }

    .button-container {
      display: flex;
      justify-content: flex-end;
      gap: 0.5rem;
      margin-top: 1rem;
    }
  `;

  private editor: ace.Ace.Editor | undefined;
  private editorInitialized = false;

  @property({ type: Object })
  bundleRequest: BundledRequestsApiModel = {};
  
  @property({ type: Array})
  projects: ProjectApiModel[] | null  = [];

  @property({ type: Array })
  existingBundleNames: string[] = [];

  @property({ type: Boolean })
  isEdit = false;

  @property({ type: Object })
  dialog!: BundleEditorDialog;

  @state()
  private readonly _typeOptions = [
    { value: 'JobRequest', label: 'JobRequest' },
    { value: 'CopyEnvBuild', label: 'CopyEnvBuild' }
  ];

  private readonly _requestTemplates: Record<string, object> = {
    'JobRequest': {
      Project: '',
      BuildText: '',
      BuildUrl: '',
      DropFolder: null,
      Components: [],
      RequestProperties: []
    },
    'CopyEnvBuild': {
      TargetEnv: '',
      DataBackup: '',
      BundleName: '',
      BundleProperties: []
    }
  };

  render() {
    console.log('Rendering form, Type:', this.bundleRequest.Type, 'typeOptions:', this._typeOptions);
    return html`
      <vaadin-vertical-layout>
        <div class="field-container">
          <vaadin-combo-box
            id="bundleName"
            label="Bundle Name"
            .items="${this.existingBundleNames}"
            .value="${this.bundleRequest.BundleName || ''}"
            allow-custom-value
            @value-changed="${(e: CustomEvent) => {
              if (e.detail.value !== undefined) {
                this._updateValue('BundleName', e.detail.value);
              }
            }}"
            @custom-value-set="${(e: CustomEvent) => {
              this._updateValue('BundleName', e.detail);
            }}"
            style="width: 100%;"
            placeholder="Select or enter Bundle Name"
            helper-text="Select an existing bundle or type a new name"
          ></vaadin-combo-box>
        </div>

        <div class="field-container">
          <vaadin-combo-box
            id="bundleType"
            label="Type"
            .items="${this._typeOptions}"
            item-label-path="label"
            item-value-path="value"
            .value="${(this.bundleRequest.Type as unknown as string) || ''}"
            @value-changed="${(e: CustomEvent) => {
              if (e.detail.value) {
                this._handleTypeChange(e.detail.value);
              }
            }}"
            style="width: 100%;"
            placeholder="Select a type"
            helper-text="JobRequest is a regular deployment, CopyEnvBuild copies environment state"
          ></vaadin-combo-box>
        </div>

        <div class="field-container">
          <vaadin-combo-box
            id="bundleProjectId"
            label="Project"
            .items="${this.projects}"
            item-label-path="ProjectName"
            item-value-path="ProjectId"
            .value="${this.bundleRequest.ProjectId}"
            @value-changed="${(e: CustomEvent) =>
              this._updateValue('ProjectId', parseInt(e.detail.value, 10))}"
            style="width: 100%;"
            placeholder="Select a Project"
          ></vaadin-combo-box>
        </div>

        <div class="field-container">
          <vaadin-text-field
            id="requestName"
            label="Request Name"
            .value="${this.bundleRequest.RequestName || ''}"
            @change="${(e: Event) =>
              this._updateValue(
                'RequestName',
                (e.target as HTMLInputElement).value
              )}"
            style="width: 100%;"
            placeholder="Enter Request Name"
            helper-text="Describes this request inside the bundle"
          ></vaadin-text-field>
        </div>

        <div class="field-container">
          <vaadin-number-field
            id="sequence"
            label="Sequence"
            .value="${this.bundleRequest.Sequence || 0}"
            @change="${(e: Event) =>
              this._updateValue(
                'Sequence',
                parseInt((e.target as HTMLInputElement).value, 10)
              )}"
            style="width: 100%;"
            placeholder="Enter Sequence"
            helper-text="Order of execution in the bundle, lower is first"
          ></vaadin-number-field>
        </div>

        <div id="editor" style="width: 50vw; height: 20vw;">Loading...</div>

        <div class="button-container">
          <vaadin-button theme="tertiary" @click="${this._handleCancel}">
            Cancel
          </vaadin-button>
          <vaadin-button theme="primary" @click="${this._handleSave}">
            ${this.isEdit ? 'Update' : 'Create'}
          </vaadin-button>
        </div>
      </vaadin-vertical-layout>
    `;
  }

  firstUpdated() {
    this._initOrUpdateEditor();
    this._updateTypeComboBox();
    this._setDefaultProject();
  }

  updated(changedProperties: Map<string | number | symbol, unknown>) {
    super.updated(changedProperties);

    if (changedProperties.has('bundleRequest')) {
      this._initOrUpdateEditor();
      this._updateTypeComboBox();
    }

    if (changedProperties.has('projects')) {
      this._setDefaultProject();
    }
  }

  private _setDefaultProject() {
    // If there's exactly one project and no project is selected, default to it
    if (this.projects && this.projects.length === 1 && !this.bundleRequest.ProjectId) {
      const project = this.projects[0];
      if (project.ProjectId) {
        this._updateValue('ProjectId', project.ProjectId);
      }
    }
  }

  private _updateTypeComboBox() {
    setTimeout(() => {
      const typeComboBox = this.shadowRoot?.getElementById(
        'bundleType'
      ) as ComboBox;
      if (typeComboBox && this.bundleRequest.Type !== undefined) {
        // API returns Type as string ("JobRequest", "CopyEnvBuild")
        const typeValue = this.bundleRequest.Type as unknown as string;
        const typeOption = this._typeOptions.find(
          option => option.value === typeValue
        );
        if (typeOption) {
          typeComboBox.selectedItem = typeOption;
        }
      }
    }, 10);
  }

  private _initOrUpdateEditor() {
    if (!this.shadowRoot) {
      return;
    }

    const jsonContent = this.bundleRequest.Request || '{}';

    if (this.editorInitialized && this.editor) {
      try {
        const formattedJson = JSON.stringify(JSON.parse(jsonContent), null, 2);
        this.editor.setValue(formattedJson, -1);
        // eslint-disable-next-line @typescript-eslint/no-unused-vars
      } catch (e) {
        this.editor.setValue(jsonContent, -1);
      }
      this.editor.clearSelection();
    } else {
      this.attachAceEditor(jsonContent);
    }
  }

  private _handleTypeChange(newType: string) {
    const currentType = this.bundleRequest.Type as unknown as string;

    // Skip if the type hasn't changed
    if (currentType === newType) {
      return;
    }

    // Update the type
    this._updateValue('Type', newType);

    // Check if we should apply a template
    // Apply template if Request is empty, default '{}', or matches another type's template
    const currentRequest = this.bundleRequest.Request || '{}';
    const isEmptyOrDefault = currentRequest === '{}' || currentRequest.trim() === '';

    // Check if current request matches one of the templates (user hasn't customized it)
    let isTemplate = false;
    for (const [, template] of Object.entries(this._requestTemplates)) {
      try {
        const templateJson = JSON.stringify(template, null, 2);
        const currentJson = JSON.stringify(JSON.parse(currentRequest), null, 2);
        if (templateJson === currentJson) {
          isTemplate = true;
          break;
        }
      } catch {
        // Invalid JSON, treat as customized
      }
    }

    if (isEmptyOrDefault || isTemplate) {
      const template = this._requestTemplates[newType];
      if (template) {
        const templateJson = JSON.stringify(template, null, 2);
        this._updateValue('Request', templateJson);

        // Update the editor
        if (this.editor) {
          this.editor.setValue(templateJson, -1);
          this.editor.clearSelection();
        }
      }
    }
  }

  private _updateValue(property: keyof BundledRequestsApiModel, value: any) {
    // Skip if value is NaN (from parseInt on empty string)
    if (typeof value === 'number' && isNaN(value)) {
      return;
    }

    // Skip if the value hasn't actually changed
    if (this.bundleRequest[property] == value) {
      return;
    }

    const updatedBundle = {
      ...this.bundleRequest,
      [property]: value
    };

    this.dialog.updateBundleRequest(updatedBundle);

    this.bundleRequest = updatedBundle;
  }

  private _handleCancel() {
    this.dialog.closeDialog();
  }

  private _handleSave() {
    this._synchronizeEditorWithBundleRequest();
    
    console.log('Updated Request before save:', this.bundleRequest.Request);
  
    if (!this._validateBundle()) {
      return;
    }
  
    const api = new BundledRequestsApi();
    
    console.log('Submitting bundle with Request:', this.bundleRequest.Request);

    const loadingChangeEvent = 'loading-changed';
    this.dispatchEvent(
      new CustomEvent(loadingChangeEvent, {
        detail: { loading: true },
        bubbles: true,
        composed: true
      })
    );

    const apiCall = this.isEdit
      ? api.bundledRequestsPut({ bundledRequestsApiModel: this.bundleRequest })
      : api.bundledRequestsPost({
          bundledRequestsApiModel: this.bundleRequest
        });

    apiCall.subscribe({
      next: () => {
        this.dispatchEvent(
          new CustomEvent(loadingChangeEvent, {
            detail: { loading: false },
            bubbles: true,
            composed: true
          })
        );
    
        this.dialog.closeDialog();
        console.log('Dispatching bundle-saved event from form');
        const savedEvent = new CustomEvent('bundle-saved', {
          detail: { bundleRequest: this.bundleRequest },
          bubbles: true,
          composed: true
        });
        this.dispatchEvent(savedEvent);
        console.log('Bundle-saved event dispatched');
      },
      error: (error) => {
        console.error('Error saving bundle request:', error);
        this.dispatchEvent(
          new CustomEvent(loadingChangeEvent, {
            detail: { loading: false },
            bubbles: true,
            composed: true
          })
        );
        this._showError('Failed to save bundle request');
      }
    });
  }

  /**
   * Initialize the ACE editor for JSON editing
   */
  public attachAceEditor(jsonRequest: string) {
    setTimeout(() => {
      const editorDiv = this.shadowRoot?.getElementById(
        'editor'
      ) as HTMLDivElement;
      if (!editorDiv) {
        return;
      }

      if (this.editor) {
        this.editor.destroy();
        this.editor = undefined;
      }

      this.editor = ace.edit(editorDiv);
      this.editor.renderer.attachToShadowRoot();

      this.editor.setTheme('ace/theme/terminal');
      this.editor.session.setMode('ace/mode/json');
      this.editor.getSession().setUseWorker(false);
      this.editor.setReadOnly(false);
      this.editor.setHighlightActiveLine(true);

      this.editor.setOptions({
        autoScrollEditorIntoView: true,
        enableBasicAutocompletion: false,
        enableLiveAutocompletion: false,
        placeholder: '',
        enableSnippets: false
      });

      try {
        const formattedJson = JSON.stringify(JSON.parse(jsonRequest), null, 2);
        this.editor.setValue(formattedJson, -1);
        // eslint-disable-next-line @typescript-eslint/no-unused-vars
      } catch (e: any) {
        this.editor.setValue(jsonRequest, -1);
      }

      this.editor.clearSelection();
      this.editorInitialized = true;
    }, 100);
  }

  private _synchronizeEditorWithBundleRequest(): void {
    if (this.editor) {
      const editorValue = this.editor.getValue();

      // Create a new object to ensure reactivity
      this.bundleRequest = {
        ...this.bundleRequest,
        Request: editorValue
      };
      
      // Update the dialog's copy
      this.dialog.updateBundleRequest(this.bundleRequest);
    }
  }
  
  private _validateBundle(): boolean {
    this._synchronizeEditorWithBundleRequest();
    
    if (!this.bundleRequest.BundleName) {
      this._showError('Bundle Name is required');
      return false;
    }

    if (this.bundleRequest.Type === undefined) {
      this._showError('Type is required');
      return false;
    }

    if (!this.bundleRequest.ProjectId) {
      this._showError('Project is required');
      return false;
    }

    if (!this.bundleRequest.RequestName) {
      this._showError('Request Name is required');
      return false;
    }

    if (this.bundleRequest.Request) {
      try {
        JSON.parse(this.bundleRequest.Request);
      } catch (e: any) {
        this._showError('Invalid JSON in Request field: ' + e.toString());
        return false;
      }
    } else {
      this._showError('Request is required');
      return false;
    }

    return true;
  }

  private _showError(message: string) {
    const notification = document.createElement('vaadin-notification');
    notification.setAttribute('theme', 'error');
    notification.setAttribute('position', 'top-center');
    notification.setAttribute('duration', '3000');
    notification.renderer = (root: HTMLElement) => {
      if (root.firstElementChild) {
        return;
      }
      const div = document.createElement('div');
      div.textContent = message;
      root.appendChild(div);
    };
    document.body.appendChild(notification);
    notification.open();
  }
}
