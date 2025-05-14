import { LitElement, html, css, render } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import '@vaadin/dialog';
import '@vaadin/text-field';
import '@vaadin/text-area';
import '@vaadin/number-field';
import '@vaadin/combo-box';
import '@vaadin/button';
import '@vaadin/vertical-layout';
import '@vaadin/horizontal-layout';
import {
  BundledRequestsApi,
  BundledRequestsApiModel,
  BundledRequestType
} from '../apis/dorc-api';
import { ErrorNotification } from './notifications/error-notification';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { guard } from 'lit/directives/guard.js';
import * as ace from 'ace-builds';

let editorValue: string | undefined = '';
@customElement('bundle-editor-dialog')
export class BundleEditorDialog extends LitElement {
  static styles = css`
    :host {
      display: block;
    }

    .dialog-content {
      min-width: 500px;
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

  @property({ type: Boolean })
  open = false;

  @property({ type: Object })
  bundleRequest: BundledRequestsApiModel = {
    BundleName: '',
    Type: BundledRequestType.NUMBER_1,
    RequestName: '',
    Sequence: 0,
    Request: '{}'
  };

  @property({ type: Boolean })
  isEdit = false;

  @state()
  private _typeOptions = [
    { value: BundledRequestType.NUMBER_1, label: 'JobRequest' },
    { value: BundledRequestType.NUMBER_2, label: 'CopyEnvBuild' }
  ];

  @state()
  private _projectId: number | null = null;

  render() {
    return html`
      <vaadin-dialog
        ?opened=${this.open}
        @opened-changed="${(event: DialogOpenedChangedEvent) => {
          this.open = event.detail.value;
          if (!this.open) {
            this.dispatchEvent(
              new CustomEvent('bundle-dialog-closed', {
                bubbles: true,
                composed: true
              })
            );
          }
        }}"
        header="${this.isEdit
          ? 'Edit Bundle Request'
          : 'Create Bundle Request'}"
        .renderer="${guard([], () => (root: HTMLElement) => {
          render(
            html` <vaadin-vertical-layout>
              <div class="field-container">
                <vaadin-text-field
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
                  label="Type"
                  .items="${this._typeOptions}"
                  item-label-path="label"
                  item-value-path="value"
                  .value="${this.bundleRequest.Type?.toString()}"
                  @change="${(e: CustomEvent) =>
                    this._updateValue('Type', parseInt(e.detail.value, 10))}"
                  style="width: 100%;"
                ></vaadin-combo-box>
              </div>

              <div class="field-container">
                <vaadin-text-field
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
              <div id="editor" style="width: 50vw; height: 20vw;">
                Loading...
              </div>

              <div class="button-container">
                <vaadin-button
                  theme="tertiary"
                  @click="${() => {
                    this.open = false;
                  }}"
                >
                  Cancel
                </vaadin-button>
                <vaadin-button theme="primary" @click="${this._handleSave}">
                  ${this.isEdit ? 'Update' : 'Create'}
                </vaadin-button>
              </div>
            </vaadin-vertical-layout>`,
            root
          );
        })}"
      ></vaadin-dialog>
    `;
  }

  protected firstUpdated(_changedProperties: any) {
    super.firstUpdated(_changedProperties);


  }

  private _updateValue(property: keyof BundledRequestsApiModel, value: any) {
    this.bundleRequest = {
      ...this.bundleRequest,
      [property]: value
    };
  }

  private _handleSave() {
    // Validate the fields
    if (!this._validateFields()) {
      return;
    }

    const api = new BundledRequestsApi();

    // Make API call based on whether we're editing or creating
    const apiCall = this.isEdit
      ? api.bundledRequestsPut({ bundledRequestsApiModel: this.bundleRequest })
      : api.bundledRequestsPost({
          bundledRequestsApiModel: this.bundleRequest
        });

    apiCall.subscribe(
      () => {
        // Success
        this.open = false;
        this.dispatchEvent(
          new CustomEvent('bundle-saved', {
            detail: { bundleRequest: this.bundleRequest }
          })
        );
      },
      error => {
        // Error
        console.error('Error saving bundle request:', error);
        new ErrorNotification().open();
      }
    );
  }

  private showErrorNotification(message: string) {
    const notification = new ErrorNotification();
    notification.setAttribute('errorMessage', message);
    this.shadowRoot?.appendChild(notification);
    notification.open();
  }

  private _validateFields(): boolean {
    // Basic validation
    if (!this.bundleRequest.BundleName) {
      this.showErrorNotification('Bundle Name is required');
      return false;
    }

    if (this.bundleRequest.Type === undefined) {
      this.showErrorNotification('Type is required');
      return false;
    }

    if (!this.bundleRequest.RequestName) {
      this.showErrorNotification('Request Name is required');
      return false;
    }

    // Validate JSON
    if (this.bundleRequest.Request) {
      try {
        JSON.parse(this.bundleRequest.Request);
      } catch (e: any) {
        this.showErrorNotification(
          'Invalid JSON in Request field ' + e.toString()
        );
        return false;
      }
    } else {
      this.showErrorNotification('Request is required');
      return false;
    }

    return true;
  }

  /**
   * Open the dialog to create a new bundle request
   */
  public openNew(projectId: number | null = null) {
    this.isEdit = false;
    this._projectId = projectId;
    this.bundleRequest = {
      BundleName: '',
      ProjectId: projectId,
      Type: BundledRequestType.NUMBER_1,
      RequestName: '',
      Sequence: 0,
      Request: '{}'
    };
    this.attachAceEditor(this.bundleRequest.Request || '{}');
    this.open = true;
  }

  /**
   * Open the dialog to edit an existing bundle request
   */
  public openEdit(bundle: BundledRequestsApiModel) {
    this.isEdit = true;
    this.bundleRequest = { ...bundle };
    this.attachAceEditor(this.bundleRequest.Request || '{}');
    this.open = true;
  }

  public attachAceEditor(jsonRequest: string) {
    const editorDiv = this.shadowRoot?.getElementById(
      'editor'
    ) as HTMLDivElement;

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

    this.editor.on('change', () => {
      editorValue = this.editor?.getValue();
    });

    this.editor?.setValue(JSON.stringify(jsonRequest, null, 2), 0);
    this.editor?.clearSelection();
  }
}
