import { LitElement, html, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import '@vaadin/dialog';
import {
  BundledRequestsApiModel,
  BundledRequestType,
  ProjectApiModel
} from '../apis/dorc-api';
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

  @property({ type: Array })
  projects: ProjectApiModel[] | null = [];

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

  render() {
    const renderDialog = () => html`
      <bundle-editor-form
        id="bundle-form"
        .bundleRequest="${this.bundleRequest}"
        .projects="${this.projects}"
        .isEdit="${this.isEdit}"
        .dialog="${this}"
        @bundle-saved="${(e: CustomEvent) => {
          this.dispatchEvent(
            new CustomEvent('bundle-saved', {
              bubbles: true,
              composed: true,
              detail: (e as CustomEvent).detail
            })
          );
          console.log('Bundle saved event forwarded');
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
   */
  public openNew(projects: ProjectApiModel[] | null = null) {
    this.isEdit = false;
    this.projects = projects;
    this.bundleRequest = {
      BundleName: '',
      ProjectId: 0,
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
