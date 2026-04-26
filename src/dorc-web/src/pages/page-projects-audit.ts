import '@vaadin/button';
import {
  GridCellPartNameGenerator,
  GridDataProviderCallback,
  GridDataProviderParams,
  GridItemModel,
  GridSorterDefinition
} from '@vaadin/grid';
import '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid-sorter';
import '@vaadin/horizontal-layout';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import { css, PropertyValues, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PagedDataSorting, RefDataProjectAuditApi } from '../apis/dorc-api';
import { PagedDataFilter, RefDataAuditApiModel } from '../apis/dorc-api/models';
import { GetRefDataAuditListResponseDto } from '../apis/dorc-api/models/GetRefDataAuditListResponseDto';
import { PageElement } from '../helpers/page-element';
import { getShortLogonName } from '../helpers/user-extensions';
import '../components/hegs-json-viewer';
import { HegsJsonViewer } from '../components/hegs-json-viewer';

@customElement('page-projects-audit')
export class PageProjectsAudit extends PageElement {
  @property({ type: Boolean }) loading = true;

  @property({ type: Boolean }) searching = false;

  private projectNameFilter = '';

  private userFilter = '';

  private actionFilter = '';

  static get styles() {
    return css`
      vaadin-grid#grid {
        overflow: auto;
        height: calc(100vh - 96px);
        --divider-color: var(--dorc-border-color);
      }
      vaadin-grid#grid::part(create-type) {
        background-color: var(--dorc-success-bg);
      }
      vaadin-grid#grid::part(delete-type) {
        background-color: var(--dorc-failure-bg);
      }
      .overlay {
        width: 100%;
        height: 100%;
        position: fixed;
      }
      .overlay__inner {
        width: 100%;
        height: 100%;
        position: absolute;
      }
      .overlay__content {
        left: 20%;
        position: absolute;
        top: 20%;
        transform: translate(-50%, -50%);
      }
      .spinner {
        width: 75px;
        height: 75px;
        display: inline-block;
        border-width: 2px;
        border-color: var(--dorc-border-color);
        border-top-color: var(--dorc-link-color);
        animation: spin 1s infinite linear;
        border-radius: 100%;
        border-style: solid;
      }
      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }
      .muted {
        color: var(--dorc-text-secondary);
        font-style: italic;
      }
    `;
  }

  render() {
    return html`
      <div
        class="overlay"
        style="z-index: 2"
        ?hidden="${!(this.loading || this.searching)}"
      >
        <div class="overlay__inner">
          <div class="overlay__content">
            <span class="spinner"></span>
          </div>
        </div>
      </div>
      <vaadin-grid
        id="grid"
        column-reordering-allowed
        multi-sort
        theme="compact row-stripes no-row-borders no-border"
        .dataProvider="${this.getProjectAudit}"
        .cellPartNameGenerator="${this.cellPartNameGenerator}"
        style="width: 100%; z-index: 1"
        ?hidden="${this.loading}"
      >
        <vaadin-grid-column
          path="Project.ProjectName"
          header="Project"
          .headerRenderer="${this.projectNameHeaderRenderer}"
          .renderer="${this.projectNameRenderer}"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Username"
          header="User"
          .headerRenderer="${this.userHeaderRenderer}"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Action"
          header="Action"
          .headerRenderer="${this.actionHeaderRenderer}"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-sort-column
          path="Date"
          header="Date"
          direction="desc"
          .renderer="${this.dateRenderer}"
          resizable
          auto-width
        ></vaadin-grid-sort-column>
        <vaadin-grid-column
          header="Value"
          .renderer="${this.valueRenderer}"
          resizable
          auto-width
        ></vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener('project-audit-loaded', this.auditLoaded as EventListener);
    this.addEventListener('searching-project-audit-started', this.searchingStarted as EventListener);
    this.addEventListener('searching-project-audit-finished', this.searchingFinished as EventListener);
  }

  private searchingStarted() {
    this.searching = true;
  }

  private searchingFinished() {
    this.searching = false;
  }

  private auditLoaded() {
    this.loading = false;
  }

  private cellPartNameGenerator: GridCellPartNameGenerator<RefDataAuditApiModel> = (
    _column,
    model
  ) => {
    const action = model.item?.Action;
    if (action === 'Create') return 'create-type';
    if (action === 'Delete') return 'delete-type';
    return '';
  };

  private projectNameRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<RefDataAuditApiModel>
  ) => {
    const name = model.item.Project?.ProjectName;
    if (!name) {
      render(html`<span class="muted">(deleted)</span>`, root);
      return;
    }
    render(html`<span>${name}</span>`, root);
  };

  private dateRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<RefDataAuditApiModel>
  ) => {
    const raw = model.item?.Date;
    if (!raw) {
      render(html``, root);
      return;
    }
    const dt = new Date(raw);
    const formatted = `${dt.toLocaleDateString('en-GB')} ${dt.toLocaleTimeString('en-GB')}`;
    render(html`<span>${formatted}</span>`, root);
  };

  private valueRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<RefDataAuditApiModel>
  ) => {
    const raw = model.item?.Json;
    if (!raw) {
      render(html`<span class="muted">—</span>`, root);
      return;
    }
    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch {
      // Audit row without a valid JSON payload — fall back to plain pre-wrap text.
      // lit auto-escapes ${raw} so no HTML injection regardless of content.
      render(
        html`<pre style="white-space: pre-wrap; margin: 0; font-size: 11px;">${raw}</pre>`,
        root
      );
      return;
    }
    // Use lit's property binding (.data) so the parsed object is passed straight to
    // the viewer's data property — no innerHTML interpolation, no XSS surface.
    render(
      html`<hegs-json-viewer style="font-size: small" .data=${parsed}></hegs-json-viewer>`,
      root
    );
    const viewer = root.querySelector('hegs-json-viewer') as unknown as HegsJsonViewer;
    viewer?.expand('*');
  };

  projectNameHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing-xs">
          <vaadin-grid-sorter path="Project.ProjectName"></vaadin-grid-sorter>
          <vaadin-text-field
            placeholder="Project"
            clear-button-visible
            focus-target
            style="width: 100px"
            theme="small"
            @input="${(e: InputEvent) => {
              const tf = e.target as HTMLInputElement;
              this.projectNameFilter = tf?.value ?? '';
              this.refreshGrid();
            }}"
          ></vaadin-text-field>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  userHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing-xs">
          <vaadin-grid-sorter path="Username">User</vaadin-grid-sorter>
          <vaadin-text-field
            placeholder="User"
            clear-button-visible
            focus-target
            style="width: 100px"
            theme="small"
            @input="${(e: InputEvent) => {
              const tf = e.target as HTMLInputElement;
              this.userFilter = tf?.value ?? '';
              this.refreshGrid();
            }}"
          ></vaadin-text-field>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  actionHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing-xs">
          <vaadin-grid-sorter path="Action">Action</vaadin-grid-sorter>
          <vaadin-text-field
            placeholder="Action"
            clear-button-visible
            focus-target
            style="width: 80px"
            theme="small"
            @input="${(e: InputEvent) => {
              const tf = e.target as HTMLInputElement;
              this.actionFilter = tf?.value ?? '';
              this.refreshGrid();
            }}"
          ></vaadin-text-field>
        </vaadin-horizontal-layout>
      `,
      root
    );
  };

  private refreshGrid() {
    this.dispatchEvent(
      new CustomEvent('searching-project-audit-started', {
        detail: {},
        bubbles: true,
        composed: true
      })
    );
    this.shadowRoot?.querySelector('vaadin-grid')?.clearCache();
  }

  getProjectAudit = (
    params: GridDataProviderParams<RefDataAuditApiModel>,
    callback: GridDataProviderCallback<RefDataAuditApiModel>
  ) => {
    const filters: PagedDataFilter[] = [];
    if (this.projectNameFilter) {
      filters.push({ Path: 'Project.Name', FilterValue: this.projectNameFilter });
    }
    if (this.userFilter) {
      filters.push({ Path: 'Username', FilterValue: this.userFilter });
    }
    if (this.actionFilter) {
      filters.push({ Path: 'Action', FilterValue: this.actionFilter });
    }

    const api = new RefDataProjectAuditApi();
    api
      .refDataProjectAuditPut({
        pagedDataOperators: {
          Filters: filters,
          SortOrders: params.sortOrders.map(
            (s: GridSorterDefinition): PagedDataSorting => ({
              Path: s.path,
              Direction: s.direction?.toString()
            })
          )
        },
        limit: params.pageSize,
        page: params.page + 1
      })
      .subscribe({
        next: (data: GetRefDataAuditListResponseDto) => {
          data.Items?.forEach(
            item => (item.Username = getShortLogonName(item.Username))
          );
          this.dispatchEvent(
            new CustomEvent('searching-project-audit-finished', {
              detail: {},
              bubbles: true,
              composed: true
            })
          );
          callback(data.Items ?? [], data.TotalItems);
        },
        error: (err: any) => console.error(err),
        complete: () => {
          this.dispatchEvent(
            new CustomEvent('project-audit-loaded', {
              detail: {},
              bubbles: true,
              composed: true
            })
          );
        }
      });
  };
}
