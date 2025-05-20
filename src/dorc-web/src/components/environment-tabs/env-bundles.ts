import { css, PropertyValues, render } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import { customElement, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageEnvBase } from './page-env-base';
import '@vaadin/details';
import '@vaadin/horizontal-layout';
import '../attached-app-users';
import {
  BundledRequestsApi,
  BundledRequestsApiModel, BundledRequestType,
} from '../../apis/dorc-api';
import { ErrorNotification } from '../notifications/error-notification.ts';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { Grid, GridItemModel } from '@vaadin/grid';
import { HegsJsonViewer } from '../hegs-json-viewer.ts';
import '../grid-button-groups/bundle-request-controls';
import '../bundle-editor-dialog';
import { BundleEditorDialog } from '../bundle-editor-dialog';

@customElement('env-bundles')
export class EnvBundles extends PageEnvBase {
  static get styles() {
    return css`
      :host {
        display: flex;
        width: 100%;
        height: 100%;
        flex-direction: column;
      }

      vaadin-grid {
        height: 100%;
      }
      
      .details-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        width: 100%;
        padding: 8px;
      }
      
      .action-buttons {
        margin-right: 8px;
      }
    `;
  }

  private bundledRequests: Array<BundledRequestsApiModel> = [];
  
  @query('bundle-editor-dialog')
  private bundleEditorDialog!: BundleEditorDialog;

  @query('#grid') grid: Grid | undefined;

  render() {
    return html`
      <vaadin-details
        opened
        summary="Bundles"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px;"
      >
        <vaadin-button
          theme="primary small"
          @click=${this._openAddBundleDialog}
        >
          Add
        </vaadin-button>
      </vaadin-details>
      
      <vaadin-grid
        id="grid"
        .items="${this.bundledRequests}"
        column-reordering-allowed
        multi-sort
        theme="compact row-stripes no-row-borders no-border"
        multi-sort-priority="append"
      >
        <vaadin-grid-sort-column
          path="BundleName"
          header="Bundle Name"
          auto-width
          flex-grow="0"
          resizable
          direction="asc"
        ></vaadin-grid-sort-column>
        <vaadin-grid-column
          .renderer="${this._typeRenderer}"
          header="Type"
          auto-width
          flex-grow="0"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="RequestName"
          header="Request Name"
          auto-width
          flex-grow="0"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-sort-column
          path="Sequence"
          header="Sequence"
          auto-width
          flex-grow="0"
          resizable
          direction="asc"
        ></vaadin-grid-sort-column>
        <vaadin-grid-column
          .renderer="${this.bundleControlsRenderer}"
          resizable
          flex-grow="0"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Request"
          header="Request"
          resizable
          .renderer="${this._jsonRenderer}"
        ></vaadin-grid-column>
      </vaadin-grid>
      
      <bundle-editor-dialog
        @bundle-saved=${this._handleBundleSaved}
      ></bundle-editor-dialog>
    `;
  }

  protected override firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener('edit-bundle-request', this._handleEditBundle as EventListener);
    this.addEventListener('delete-bundle-request', this._handleDeleteBundle as EventListener);
  }

  private _openAddBundleDialog() {
    // Get the project ID from the first mapped project if available
    const projectId = this.envContent?.MappedProjects?.[0]?.ProjectId || null;
    this.bundleEditorDialog.openNew(projectId);
  }
  
  private _handleBundleSaved(e: CustomEvent) {
    console.log('Bundle saved event received in env-bundles', e.detail);
    // Refresh the bundle list
    this.fetchBundledRequests();
  }

  bundleControlsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<BundledRequestsApiModel>
  ) {
    render(
      html`<bundle-request-controls 
        .value="${model.item}"
        @edit-click=${() => {this.dispatchEvent(
          new CustomEvent('edit-bundle-request', {
            detail: {
              value: model.item
            },
            bubbles: true,
            composed: true
          })
        );}}
        @delete-click=${(e: CustomEvent) => {this.dispatchEvent(
          new CustomEvent('delete-bundle-request', {
            detail: e.detail,
            bubbles: true,
            composed: true
          })
        );}}
      >
      </bundle-request-controls>`,
      root
    );
  }

  private _handleEditBundle(e: CustomEvent) {
    this.bundleEditorDialog.openEdit(e.detail.value);
  }
  
  private _handleDeleteBundle(e: CustomEvent) {
    if (e.detail.value.bundleId) {
      // Confirm before deleting
      const confirmDelete = confirm('Are you sure you want to delete this bundle request?');
      
      if (confirmDelete) {
        const api = new BundledRequestsApi();
        api.bundledRequestsDelete({ id: e.detail.value.bundleId }).subscribe(
          () => {
            // Success - refresh the grid
            this.fetchBundledRequests();
          },
          error => {
            console.error('Error deleting bundle request:', error);
            new ErrorNotification().open();
          }
        );
      }
    }
  }

  _jsonRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<BundledRequestsApiModel>
  ) {
    const bundle = model.item as BundledRequestsApiModel;

    root.innerHTML = `<hegs-json-viewer style="font-size: small ">${
      bundle.Request
    }</hegs-json-viewer>`;
    const viewer = root.querySelector(
      'hegs-json-viewer'
    ) as unknown as HegsJsonViewer;
    viewer.expand('**');
  }

  _typeRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<BundledRequestsApiModel>
  ) {
    const bundle = model.item as BundledRequestsApiModel;

    let typeString = '';

    if (bundle.Type === BundledRequestType.NUMBER_1) {
      typeString = 'JobRequest'
    } else if (bundle.Type === BundledRequestType.NUMBER_2) {
      typeString = 'CopyEnvBuild'
    } else {
      typeString = 'Unknown';
    }

    root.innerHTML = `<span>${typeString}</span>`;
  }

  private fetchBundledRequests() {
    const api = new BundledRequestsApi();
    const projs: string[] =
      this.envContent?.MappedProjects?.map(p => p.ProjectName || '').filter(
        name => name
      ) || [];
    api.bundledRequestsGet({ projectNames: projs }).subscribe(
      data => {
        // Sort the data by BundleName first, then by Sequence
        this.bundledRequests = data.sort((a, b) => {
          // First sort by BundleName
          const nameCompare = (a.BundleName || '').localeCompare(b.BundleName || '');
          
          // If names are equal, sort by Sequence
          if (nameCompare === 0) {
            const seqA = a.Sequence || 0;
            const seqB = b.Sequence || 0;
            return seqA - seqB;
          }
          
          return nameCompare;
        });
        
        // Force grid to re-render with new data
        if (this.grid) {
          this.grid.clearCache();
          this.requestUpdate();
        }
      },
      error => {
        console.error('Error fetching bundled requests:', error);
        new ErrorNotification().open();
      }
    );
  }

  constructor() {
    super();
    super.loadEnvironmentInfo();
  }

  override notifyEnvironmentContentReady(){
    this.fetchBundledRequests();
  }
}
