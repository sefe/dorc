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
  BundledRequestsApi
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
      margin-bottom: 1rem;
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
  private jsonRequest = '{}';

  @property({ type: Object })
  bundleRequest: BundledRequestsApiModel = {};

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
            @change="${(e: Event) =>
              this._updateValue(
                'BundleName',
                (e.target as HTMLInputElement).value
              )}"
            style="width: 100%;"
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
            @change="${(e: CustomEvent) =>
              this._updateValue('Type', parseInt(e.detail.value, 10))}"
            style="width: 100%;"
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

  /**
   * Setup ACE editor once the component is first rendered
   */
  firstUpdated() {
    this._initOrUpdateEditor();
    this._updateTypeComboBox();
  }

  /**
   * Update when properties change
   */
  updated(changedProperties: Map<string | number | symbol, unknown>) {
    super.updated(changedProperties);

    // React to bundleRequest changes
    if (changedProperties.has('bundleRequest')) {
      this._initOrUpdateEditor();
      this._updateTypeComboBox();
    }
  }

  /**
   * Update the type combo box with the correct value
   */
  private _updateTypeComboBox() {
    setTimeout(() => {
      const typeComboBox = this.shadowRoot?.getElementById(
        'bundleType'
      ) as ComboBox;
      if (typeComboBox && this.bundleRequest.Type !== undefined) {
        typeComboBox.value = this.bundleRequest.Type;
      }
    }, 10);
  }

  /**
   * Initialize or update the editor
   */
  private _initOrUpdateEditor() {
    if (!this.shadowRoot) {
      return;
    }

    const jsonContent = this.bundleRequest.Request || '{}';

    if (this.editorInitialized && this.editor) {
      // Update the existing editor
      try {
        const formattedJson = JSON.stringify(JSON.parse(jsonContent), null, 2);
        this.editor.setValue(formattedJson, -1);
      } catch (e: any) {
        this.editor.setValue(jsonContent, -1);
      }
      this.editor.clearSelection();
    } else {
      // Initialize new editor
      this.attachAceEditor(jsonContent);
    }
  }

  /**
   * Update handler for form fields
   */
  private _updateValue(property: keyof BundledRequestsApiModel, value: any) {
    const updatedBundle = {
      ...this.bundleRequest,
      [property]: value
    };

    // Update the parent dialog's copy of the bundle request
    this.dialog.updateBundleRequest(updatedBundle);

    // Also update the local copy
    this.bundleRequest = updatedBundle;
  }

  /**
   * Cancel button handler
   */
  private _handleCancel() {
    this.dialog.closeDialog();
  }

  /**
   * Save/Update button handler
   */
  private _handleSave() {
    // Update the JSON request from the editor before saving
    if (this.editor) {
      const editorValue = this.editor.getValue();
      this._updateValue('Request', editorValue);
    }

    // Validate the bundle request before saving
    if (!this._validateBundle()) {
      return;
    }

    // Make the API call to save the updates
    const api = new BundledRequestsApi();

    // Show loading state
    const loadingChangeEvent = 'loading-changed';
    this.dispatchEvent(
      new CustomEvent(loadingChangeEvent, {
        detail: { loading: true },
        bubbles: true,
        composed: true
      })
    );

    // Make API call based on whether we're editing or creating
    const apiCall = this.isEdit
      ? api.bundledRequestsPut({ bundledRequestsApiModel: this.bundleRequest })
      : api.bundledRequestsPost({
          bundledRequestsApiModel: this.bundleRequest
        });

    apiCall.subscribe(
      () => {
        // Success
        this.dispatchEvent(
          new CustomEvent(loadingChangeEvent, {
            detail: { loading: false },
            bubbles: true,
            composed: true
          })
        );

        // Close the dialog and notify of a successful save
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
      error => {
        // Error
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
    );
  }

  /**
   * Initialize the ACE editor for JSON editing
   */
  public attachAceEditor(jsonRequest: string) {
    setTimeout(() => {
      // Use setTimeout to ensure the DOM is ready
      const editorDiv = this.shadowRoot?.getElementById(
        'editor'
      ) as HTMLDivElement;
      if (!editorDiv) {
        return;
      }

      // If an editor already exists, destroy it first
      if (this.editor) {
        this.editor.destroy();
        this.editor = undefined;
      }

      // Initialize ACE editor
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

      // Update the bundleRequest Request field when the editor content changes
      this.editor.on('change', () => {
        if (this.editor) {
          this.jsonRequest = this.editor.getValue();
          // Don't update in real-time to avoid constant re-renders
          // We'll capture the value when save is clicked
        }
      });

      // Try to parse and format the JSON for better display
      try {
        const formattedJson = JSON.stringify(JSON.parse(jsonRequest), null, 2);
        this.editor.setValue(formattedJson, -1);
      } catch (e: any) {
        // If parsing fails, just set the raw value
        this.editor.setValue(jsonRequest, -1);
      }

      this.editor.clearSelection();
      this.editorInitialized = true;
    }, 100);
  }

  /**
   * Basic validation for the bundle request
   */
  private _validateBundle(): boolean {
    if (!this.bundleRequest.BundleName) {
      this._showError('Bundle Name is required');
      return false;
    }

    if (this.bundleRequest.Type === undefined) {
      this._showError('Type is required');
      return false;
    }

    if (!this.bundleRequest.RequestName) {
      this._showError('Request Name is required');
      return false;
    }

    // Validate JSON
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

  /**
   * Helper method to show error notifications
   */
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
