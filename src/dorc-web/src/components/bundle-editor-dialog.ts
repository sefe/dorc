import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import '@vaadin/dialog';
import { BundledRequestsApi, BundledRequestsApiModel, BundledRequestType } from '../apis/dorc-api';
import { ErrorNotification } from './notifications/error-notification';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogRenderer } from '@vaadin/dialog/lit';
import './bundle-editor-form';

@customElement('bundle-editor-dialog')
export class BundleEditorDialog extends LitElement {
  static styles = css`
    :host {
      display: block;
    }
  `;

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
  private loading = false;

  render() {
    // Create a unique key that will change when any of the bundle properties change
    const bundleKey = JSON.stringify({
      id: this.bundleRequest.Id,
      name: this.bundleRequest.BundleName,
      type: this.bundleRequest.Type,
      requestName: this.bundleRequest.RequestName,
      isEdit: this.isEdit
    });
    
    const renderDialog = () => html`
      <bundle-editor-form
        id="bundle-form"
        .bundleRequest="${this.bundleRequest}"
        .isEdit="${this.isEdit}"
        .dialog="${this}"
      ></bundle-editor-form>
    `;
  
    return html`
      <vaadin-dialog
        ?opened=${this.open}
        header-title="${this.isEdit ? 'Edit Bundle Request' : 'Create Bundle Request'}"
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
        ${dialogRenderer(renderDialog, [bundleKey])}
      ></vaadin-dialog>
    `;
  }

  /**
   * Handles the save action triggered from the form
   */
  public handleSave(bundleRequest: BundledRequestsApiModel) {
    if (!this._validateBundle(bundleRequest)) {
      return;
    }

    this.loading = true;
    const api = new BundledRequestsApi();

    // Make API call based on whether we're editing or creating
    const apiCall = this.isEdit
      ? api.bundledRequestsPut({ bundledRequestsApiModel: bundleRequest })
      : api.bundledRequestsPost({ bundledRequestsApiModel: bundleRequest });

    apiCall.subscribe(
      () => {
        // Success
        this.loading = false;
        this.open = false;
        this.dispatchEvent(
          new CustomEvent('bundle-saved', {
            detail: { bundleRequest },
            bubbles: true,
            composed: true
          })
        );
      },
      error => {
        // Error
        console.error('Error saving bundle request:', error);
        this.loading = false;
        new ErrorNotification().open();
      }
    );
  }

  /**
   * Basic validation for the bundle request
   */
  private _validateBundle(bundle: BundledRequestsApiModel): boolean {
    if (!bundle.BundleName) {
      this._showError('Bundle Name is required');
      return false;
    }

    if (bundle.Type === undefined) {
      this._showError('Type is required');
      return false;
    }

    if (!bundle.RequestName) {
      this._showError('Request Name is required');
      return false;
    }

    // Validate JSON
    if (bundle.Request) {
      try {
        JSON.parse(bundle.Request);
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
    const notification = new ErrorNotification();
    notification.setAttribute('errorMessage', message);
    document.body.appendChild(notification);
    notification.open();
  }

  /**
   * Close the dialog
   */
  public closeDialog() {
    this.open = false;
  }

  /**
   * Update the bundle request
   */
  public updateBundleRequest(bundleRequest: BundledRequestsApiModel) {
    this.bundleRequest = bundleRequest;
  }

  /**
   * Open the dialog to create a new bundle request
   */
  public openNew(projectId: number | null = null) {
    this.isEdit = false;
    this.bundleRequest = {
      BundleName: '',
      ProjectId: projectId,
      Type: BundledRequestType.NUMBER_1,
      RequestName: '',
      Sequence: 0,
      Request: '{}'
    };
    this.open = true;
  }

  /**
   * Open the dialog to edit an existing bundle request
   */
  public openEdit(bundle: BundledRequestsApiModel) {
    this.isEdit = true;
    this.bundleRequest = { ...bundle };
    this.open = true;
  }
}
