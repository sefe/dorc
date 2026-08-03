import { ref } from 'lit/directives/ref.js';
import { columnBodyRenderer, columnHeaderRenderer } from '@vaadin/grid/lit';
import { confirmPrompt } from '../components/confirm-prompt';
import { css, PropertyValues } from 'lit';
import '../components/dorc-spinner';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/combo-box';
import '@vaadin/button';
import '@vaadin/icon';
import '@vaadin/icons';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import '@vaadin/details';
import '@vaadin/horizontal-layout';
import { BundledRequestsApi, BundledRequestsApiModel, RefDataProjectEnvironmentMappingsApi, EnvironmentApiModelTemplateApiModel } from '../apis/dorc-api';
import { ErrorNotification } from '../components/notifications/error-notification.ts';
import { Grid } from '@vaadin/grid';
import { HegsJsonViewer } from '../components/hegs-json-viewer.ts';
import '../components/grid-button-groups/bundle-request-controls';
import '../components/bundle-editor-dialog';
import { BundleEditorDialog } from '../components/bundle-editor-dialog';
import { navigate } from '../router/router';
import { ComboBox } from '@vaadin/combo-box';
import '@vaadin/grid/vaadin-grid-sorter';

/**
 * Fully expands a `hegs-json-viewer` once Lit has created it.
 *
 * The viewer only exposes expansion as a method, so this rides the `ref`
 * directive rather than querying the element back out of the grid cell.
 */
const expandJsonViewer = (element?: Element) => {
  (element as unknown as HegsJsonViewer | undefined)?.expand('**');
};

@customElement('page-project-bundles')
export class PageProjectBundles extends ResponsiveMixin(PageElement) {
  @property({ type: String })
  project: string | undefined;

  @property({ type: Boolean })
  private loading = true;

  @property({ type: String })
  private bundleNameFilter = '';

  static get styles() {
    return css`
      :host {
        display: flex;
        width: 100%;
        height: 100%;
        flex-direction: column;
      }

      .header {
        display: flex;
        align-items: center;
        gap: 10px;
        margin-bottom: var(--lumo-space-m);
        padding: var(--lumo-space-m) var(--lumo-space-m) 0 var(--lumo-space-m);
        flex-wrap: wrap;
      }



      h2 {
        margin: 0;
        color: #333;
      }

      vaadin-grid {
        height: 100%;
      }

      @media (max-width: 768px) {
        vaadin-grid-cell-content {
          white-space: normal;
          word-wrap: break-word;
          overflow-wrap: break-word;
        }
      }
    `;
  }

  private bundledRequests: Array<BundledRequestsApiModel> = [];
  private filteredBundledRequests: Array<BundledRequestsApiModel> = [];
  private projectData: EnvironmentApiModelTemplateApiModel | undefined;

  @state()
  private uniqueBundleNames: string[] = [];

  @query('bundle-editor-dialog')
  private bundleEditorDialog!: BundleEditorDialog;

  @query('#grid') grid: Grid | undefined;

  // Bound header renderer to avoid creating new function references on each render
  private _boundHandleBundleNameFilterChange = this.handleBundleNameFilterChange.bind(this);

  private updateUniqueBundleNames() {
    const names = new Set<string>();
    this.bundledRequests.forEach(bundle => {
      if (bundle.BundleName) {
        names.add(bundle.BundleName);
      }
    });
    this.uniqueBundleNames = Array.from(names).sort();
  }

  private applyBundleNameFilter() {
    if (!this.bundleNameFilter) {
      this.filteredBundledRequests = [...this.bundledRequests];
    } else {
      this.filteredBundledRequests = this.bundledRequests.filter(bundle => 
        bundle.BundleName === this.bundleNameFilter
      );
    }
  }

  private handleBundleNameFilterChange(e: CustomEvent) {
    const comboBox = e.target as ComboBox;
    this.bundleNameFilter = comboBox.value || '';
    this.applyBundleNameFilter();
  }

  bundleNameHeaderRenderer() {
    return html`
        <vaadin-grid-sorter
          path="BundleName"
          direction="asc"
          style="align-items: normal"
        >Bundle Name</vaadin-grid-sorter>
        <vaadin-combo-box
          clear-button-visible
          focus-target
          .items="${this.uniqueBundleNames}"
          placeholder="Select bundle..."
          style="width: 200px"
          theme="small"
          .value="${this.bundleNameFilter}"
          @value-changed="${this._boundHandleBundleNameFilterChange}"
        ></vaadin-combo-box>
      `;
  }

  render() {
    return html`
      <dorc-spinner
        style="--dorc-spinner-z-index: 1000"
        ?hidden="${!this.loading}"
      ></dorc-spinner>

      <div class="header">
        <h2>${this.project} Bundles</h2>
      </div>

      <vaadin-details
        opened
        summary="Bundles"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; margin: 0px;"
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
        .items="${this.filteredBundledRequests}"
        column-reordering-allowed
        multi-sort
        theme="compact row-stripes no-row-borders no-border"
        multi-sort-priority="append"
      >
        <vaadin-grid-column
          path="BundleName"
          header="Bundle Name"
          auto-width
          flex-grow="0"
          resizable
          ${columnHeaderRenderer(this.bundleNameHeaderRenderer, [])}
        ></vaadin-grid-column>
        <vaadin-grid-column
          ${columnBodyRenderer(this._typeRenderer, [])}
          header="Type"
          auto-width
          flex-grow="0"
          resizable
          ?hidden="${this._narrowScreen}"
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
          ?hidden="${this._narrowScreen}"
        ></vaadin-grid-sort-column>
        <vaadin-grid-column
          ${columnBodyRenderer(this.bundleControlsRenderer, [])}
          resizable
          flex-grow="0"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Request"
          header="Request"
          resizable
          ${columnBodyRenderer(this._jsonRenderer, [])}
          ?hidden="${this._narrowScreen}"
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
      this._handleDeleteBundle as unknown as EventListener
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
            void navigate('not-found');
          }
        );
    }
  }

  private _openAddBundleDialog() {
    const projects = this.projectData?.Project ? [this.projectData.Project] : [];
    this.bundleEditorDialog.openNew(projects, this.uniqueBundleNames);
  }

  private _handleBundleSaved() {
    this.fetchBundledRequests();
  }

  bundleControlsRenderer(
    item: BundledRequestsApiModel
  ) {
    return html` <bundle-request-controls .value="${item}">
      </bundle-request-controls>`;
  }

  private _handleEditBundle(e: CustomEvent) {
    const projects = this.projectData?.Project ? [this.projectData.Project] : [];
    this.bundleEditorDialog.openEdit(e.detail.value, projects, this.uniqueBundleNames);
  }

  private async _handleDeleteBundle(e: CustomEvent) {
    const bundle = e.detail.value as BundledRequestsApiModel;

    if (bundle.Id) {
      const confirmDelete = await confirmPrompt(
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

  _jsonRenderer(bundle: BundledRequestsApiModel) {
    // The viewer is configured through a method, so `ref` fires exactly when
    // the element exists rather than querying it back out of the cell.
    return html`<hegs-json-viewer
      style="font-size: small"
      ${ref(expandJsonViewer)}
      >${bundle.Request}</hegs-json-viewer
    >`;
  }

  _typeRenderer(bundle: BundledRequestsApiModel) {
    // API returns Type as string name ("JobRequest", "CopyEnvBuild") not number
    return html`<span>${(bundle.Type as unknown as string) || 'Unknown'}</span>`;
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

        this.updateUniqueBundleNames();
        this.applyBundleNameFilter();

        if (this.grid) {
          this.grid.clearCache();
          this.grid.requestContentUpdate();
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