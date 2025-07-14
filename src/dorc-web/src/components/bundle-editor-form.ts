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
  BundledRequestType,
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

  @property({ type: Boolean })
  isEdit = false;

  @property({ type: Object })
  dialog!: BundleEditorDialog;

  @state()
  private readonly _typeOptions = [
    { value: BundledRequestType.NUMBER_1, label: 'JobRequest' },
    { value: BundledRequestType.NUMBER_2, label: 'CopyEnvBuild' }
  ];

  render() {
    return html`
      <vaadin-vertical-layout>
        <div class="field-container">
          <vaadin-text-field
            id="bundleName"
            label="Bundle Name"
            .value="${this.bundleRequest.BundleName || ''}"
            @input="${(e: Event) =>
              this._updateValue(
                'BundleName',
                (e.target as HTMLInputElement).value
              )}"
            style="width: 100%;"
            placeholder="Enter Bundle Name"
            helper-text="To add to an existing Bundle, use the same name"
          ></vaadin-text-field>
        </div>

        <div class="field-container">
          <vaadin-combo-box
            id="bundleType"
            label="Type"
            .items="${this._typeOptions}"
            item-label-path="label"
            item-value-path="value"
            .value="${this.bundleRequest.Type}"
            @value-changed="${(e: CustomEvent) =>
              this._updateValue('Type', parseInt(e.detail.value, 10))}"
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
              this._updateValue('ProjectId', 
                (e.detail.value !== null && e.detail.value !== '') 
                ? parseInt(e.detail.value, 10) 
                : 0)}"
            style="width: 100%;"
            placeholder="Select a Project"
          ></vaadin-combo-box>
        </div>-

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
  }

  updated(changedProperties: Map<string | number | symbol, unknown>) {
    super.updated(changedProperties);

    if (changedProperties.has('bundleRequest')) {
      this._initOrUpdateEditor();
      this._updateTypeComboBox();
    }
  }

  private _updateTypeComboBox() {
    setTimeout(() => {
      const typeComboBox = this.shadowRoot?.getElementById(
        'bundleType'
      ) as ComboBox;
      if (typeComboBox && this.bundleRequest.Type !== undefined) {
        if (typeComboBox.value)
        {
          const typeOption = this._typeOptions.find(
            option => option.value === this.bundleRequest.Type
          );
          if (typeOption) {
            typeComboBox.selectedItem = typeOption;
          }
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

  private _updateValue(property: keyof BundledRequestsApiModel, value: any) {
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
