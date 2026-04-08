import { LitElement, html, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import '@vaadin/dialog';
import { BundledRequestsApiModel, BundledRequestType, ProjectApiModel } from '../apis/dorc-api';
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

  @property({ type: Array})
  projects: ProjectApiModel[] | null  = [];

  @property({ type: Array })
  existingBundleNames: string[] = [];

  @property({ type: Boolean })
  open = false;

  @property({ type: Object })
  bundleRequest: BundledRequestsApiModel = {
    BundleName: '',
    Type: BundledRequestType.JobRequest,
    RequestName: '',
    Sequence: 0,
    Request: '{}'
  };

  @property({ type: Boolean })
  isEdit = false;

  render() {
    const renderDialog = () => html`
      <bundle-editor-form
        id="bundle-form"
        .bundleRequest="${this.bundleRequest}"
        .projects="${this.projects}"
        .existingBundleNames="${this.existingBundleNames}"
        .isEdit="${this.isEdit}"
        .dialog="${this}"
        @bundle-saved="${(e: CustomEvent) => {
          this.dispatchEvent(
            new CustomEvent('bundle-saved', {
              bubbles: true,
              composed: true,
              detail: e.detail
            })
          );
        }}"
            
      ></bundle-editor-form>
    `;

    return html`
      <vaadin-dialog
        ?opened=${this.open}
        header-title="${this.isEdit
          ? 'Edit Bundle Request'
          : 'Create Bundle Request'}"
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
        ${dialogRenderer(renderDialog, [])}
      ></vaadin-dialog>
    `;
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
   * @param projects - List of available projects for the bundle request
   * @param existingBundleNames - List of existing bundle names for autocomplete suggestions
   */
  public openNew(projects: ProjectApiModel[] | null = null, existingBundleNames: string[] = []) {
    this.isEdit = false;
    this.projects = projects;
    this.existingBundleNames = existingBundleNames;
    this.bundleRequest = {
      BundleName: '',
      ProjectId: 0,
      Type: BundledRequestType.JobRequest,
      RequestName: '',
      Sequence: 0,
      Request: '{}'
    };
    this.open = true;
  }

  /**
   * Open the dialog to edit an existing bundle request
   * @param bundle - The bundle request to edit
   * @param projects - List of available projects for the bundle request
   * @param existingBundleNames - List of existing bundle names for autocomplete suggestions
   */
  public openEdit(bundle: BundledRequestsApiModel, projects: ProjectApiModel[] | null = null, existingBundleNames: string[] = []) {
    this.isEdit = true;
    this.projects = projects;
    this.existingBundleNames = existingBundleNames;
    this.bundleRequest = { ...bundle };
    this.open = true;
  }
}
