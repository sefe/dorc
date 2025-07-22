import { css, PropertyValues, render } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icon';
import '@vaadin/icons';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import '@vaadin/details';
import '@vaadin/horizontal-layout';
import {
  BundledRequestsApi,
  BundledRequestsApiModel,
  BundledRequestType,
  RefDataProjectEnvironmentMappingsApi,
  EnvironmentApiModelTemplateApiModel
} from '../apis/dorc-api';
import { ErrorNotification } from '../components/notifications/error-notification.ts';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { Grid, GridItemModel } from '@vaadin/grid';
import { HegsJsonViewer } from '../components/hegs-json-viewer.ts';
import '../components/grid-button-groups/bundle-request-controls';
import '../components/bundle-editor-dialog';
import { BundleEditorDialog } from '../components/bundle-editor-dialog';
import { Router } from '@vaadin/router';

@customElement('page-project-bundles')
export class PageProjectBundles extends PageElement {
  @property({ type: String })
  project: string | undefined;

  @property({ type: Boolean })
  private loading = true;

  static get styles() {
    return css`
      :host {
        display: flex;
        width: 100%;
        height: 100%;
        flex-direction: column;
        padding: 20px;
      }

      .header {
        display: flex;
        align-items: center;
        gap: 10px;
        margin-bottom: 20px;
      }



      h2 {
        margin: 0;
        color: #333;
      }

      vaadin-grid {
        height: 100%;
      }

      .overlay {
        width: 100%;
        height: 100%;
        position: fixed;
        top: 0;
        left: 0;
        background: rgba(255, 255, 255, 0.8);
        z-index: 1000;
      }

      .overlay__inner {
        width: 100%;
        height: 100%;
        position: absolute;
        display: flex;
        align-items: center;
        justify-content: center;
      }

      .spinner {
        width: 75px;
        height: 75px;
        display: inline-block;
        border-width: 2px;
        border-color: rgba(255, 255, 255, 0.05);
        border-top-color: cornflowerblue;
        animation: spin 1s infinite linear;
        border-radius: 100%;
        border-style: solid;
      }

      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }
    `;
  }

  private bundledRequests: Array<BundledRequestsApiModel> = [];
  private projectData: EnvironmentApiModelTemplateApiModel | undefined;

  @query('bundle-editor-dialog')
  private bundleEditorDialog!: BundleEditorDialog;

  @query('#grid') grid: Grid | undefined;

  render() {
    return html`
      <div class="overlay" ?hidden="${!this.loading}">
        <div class="overlay__inner">
          <span class="spinner"></span>
        </div>
      </div>

      <div class="header">
        <h2>Bundles for ${this.project}</h2>
      </div>

      <vaadin-details
        opened
        summary="Bundles"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px;"
      >
        <vaadin-button
          theme="primary small"
          .disabled="${true}"
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

    this.addEventListener(
      'edit-bundle-request',
      this._handleEditBundle as EventListener
    );
    this.addEventListener(
      'delete-bundle-request',
      this._handleDeleteBundle as EventListener
    );

    // Get project name from URL
    const projectName = location.pathname.split('/')[2];
    this.project = decodeURIComponent(projectName);

    this.loadProjectData();
  }



  private loadProjectData() {
    const api = new RefDataProjectEnvironmentMappingsApi();
    if (this.project !== undefined) {
      api
        .refDataProjectEnvironmentMappingsGet({
          project: this.project,
          includeRead: true
        })
        .subscribe(
          (data: EnvironmentApiModelTemplateApiModel) => {
            this.projectData = data;
            this.fetchBundledRequests();
          },
          (err: any) => {
            console.error(err);
            Router.go('not-found');
          }
        );
    }
  }

  private _openAddBundleDialog() {
    const projects = this.projectData?.Project ? [this.projectData.Project] : [];
    this.bundleEditorDialog.openNew(projects);
  }

  private _handleBundleSaved(e: CustomEvent) {
    console.log('Bundle saved event received in page-project-bundles', e.detail);
    this.fetchBundledRequests();
  }

  bundleControlsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<BundledRequestsApiModel>
  ) {
    render(
      html` <bundle-request-controls .value="${model.item}"
      .disabled="${true}"
      >
      </bundle-request-controls>`,
      root
    );
  }

  private _handleEditBundle(e: CustomEvent) {
    this.bundleEditorDialog.openEdit(e.detail.value);
  }

  private _handleDeleteBundle(e: CustomEvent) {
    const bundle = e.detail.value as BundledRequestsApiModel;

    if (bundle.Id) {
      const confirmDelete = confirm(
        'Are you sure you want to delete this bundle request: ' +
          bundle.BundleName +
          '-' + bundle.RequestName +
          '?'
      );

      if (confirmDelete) {
        const api = new BundledRequestsApi();
        api.bundledRequestsDelete({ id: bundle.Id }).subscribe({
          next: () => {
            this.fetchBundledRequests();
          },
          error: (error) => {
            console.error('Error deleting bundle request:', error);
            new ErrorNotification().open();
          }
        });
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

    let typeString: string;

    if (bundle.Type === BundledRequestType.NUMBER_1) {
      typeString = 'JobRequest';
    } else if (bundle.Type === BundledRequestType.NUMBER_2) {
      typeString = 'CopyEnvBuild';
    } else {
      typeString = 'Unknown';
    }

    root.innerHTML = `<span>${typeString}</span>`;
  }

  private fetchBundledRequests() {
    const api = new BundledRequestsApi();
    const projectNames = this.project ? [this.project] : [];
    
    api.bundledRequestsGet({ projectNames }).subscribe({
      next: data => {
        this.bundledRequests = data.sort((a, b) => {
          const nameCompare = (a.BundleName || '').localeCompare(
            b.BundleName || ''
          );

          if (nameCompare === 0) {
            const seqA = a.Sequence || 0;
            const seqB = b.Sequence || 0;
            return seqA - seqB;
          }

          return nameCompare;
        });

        if (this.grid) {
          this.grid.clearCache();
          this.requestUpdate();
        }

        this.loading = false;
      },
      error: error => {
        console.error('Error fetching bundled requests:', error);
        new ErrorNotification().open();
        this.loading = false;
      }
    });
  }
}