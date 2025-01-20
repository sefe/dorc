import { css, LitElement, render} from 'lit';
import '@vaadin/text-field';
import '@vaadin/combo-box';
import '@vaadin/button';
import '@vaadin/details';
import '@vaadin/checkbox';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/dialog';
import '@vaadin/vertical-layout';
import '@vaadin/horizontal-layout';
import type { ProjectApiModel } from '../apis/dorc-api';
import '@polymer/paper-dialog';
import '../components/hegs-dialog';
import { HegsDialog } from './hegs-dialog';
import '@vaadin/button';
import {
  GridDataProviderCallback,
  GridDataProviderParams,
  GridFilterDefinition,
  GridItemModel,
  GridSorterDefinition
} from '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-filter';
import { GridFilter } from '@vaadin/grid/vaadin-grid-filter';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import '../components/add-daemon';
import { PagedDataSorting, RefDataProjectAuditApi } from '../apis/dorc-api';
import {
  GetRefDataAuditListResponseDto,
  PagedDataFilter,
  RefDataAuditApiModel
} from '../apis/dorc-api/models';

@customElement('project-audit-data')
export class ProjectAuditData extends LitElement {
  @property({ type: Object })
  get project(): ProjectApiModel {
    return this._project;
  }

  set project(value: ProjectApiModel) {
    if (value === undefined) return;
    const oldVal = this._project;
    this._project = JSON.parse(JSON.stringify(value));

    this.requestUpdate('project', oldVal);
  }

  private _project = this.getEmptyProj();

  @property({ type: Boolean }) loading = true;
  
  @query('#dialog') dialog!: HegsDialog;
  
  static get styles() {
    return css`
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 56px);
        --divider-color: rgb(223, 232, 239);
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

      paper-dialog.size-position {
        top: 16px;
        overflow: auto;
        padding: 10px;
      }

      .highlight {
        background-color: #b4d5ff;
      }

      .highlight-removed {
        background-color: #ffb4c2;
      }
    `;
  }

  render() {
    return html`
      <hegs-dialog
        id="dialog"
        title="Project Audit"
          style="width: 1000px; height: 500px; z-index: 1"
      >
        <vaadin-grid
          id="grid"
          column-reordering-allowed
          multi-sort
          theme="compact row-stripes no-row-borders no-border"
          .dataProvider="${this.getProjectValuesAudit}"
          .cellClassNameGenerator="${this.cellClassNameGenerator}"
          style="width: 1000px; height: 500px; z-index: 1"
          ?hidden="${this.loading}"
        >
          <vaadin-grid-column
            path="Username"
            header="User"
            .headerRenderer="${this.userHeaderRenderer}"
            resizable
            auto-width
          ></vaadin-grid-column>
          <vaadin-grid-sort-column
            path="Date"
            header="Updated"
            direction="desc"
            .renderer="${this.UpdatedRenderer}"
            resizable
            auto-width
          ></vaadin-grid-sort-column>
          <vaadin-grid-column
            header="Value"
            .renderer="${this.valueRenderer}"
            .headerRenderer="${this.valueHeaderRenderer}"
            resizable
            width="60em"
          ></vaadin-grid-column>
        </vaadin-grid>
      </hegs-dialog>
    `;
  }

  getEmptyProj(): ProjectApiModel {
    return {
      ProjectDescription: '',
      ProjectId: 0,
      ProjectName: '',
      ArtefactsBuildRegex: '',
      ArtefactsSubPaths: '',
      ArtefactsUrl: ''
    };
  }
  

  private cellClassNameGenerator(
    _: GridColumn,
    model: GridItemModel<RefDataAuditApiModel>
  ) {
    const { item } = model;
    let classes = '';

    if (item.Action === 'Create') {
      classes += ' insert-type';
    }

    if (item.Action === 'Delete') {
      classes += ' delete-type';
    }
    return classes;
  }

  userHeaderRenderer(root: HTMLElement) {
    render(
      html`<vaadin-grid-sorter path="Username">User</vaadin-grid-sorter>
        <vaadin-grid-filter path="Username">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100%"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>`,
      root
    );

    const filter: GridFilter = root.querySelector(
      'vaadin-grid-filter'
    ) as GridFilter;
    root
      .querySelector('vaadin-text-field')!
      .addEventListener('value-changed', (e: CustomEvent) => {
        filter.value = e.detail.value;
        this.dispatchEvent(
          new CustomEvent('searching-audit-started', {
            detail: {},
            bubbles: true,
            composed: true
          })
        );
      });
  }

  valueHeaderRenderer(root: HTMLElement) {
    render(
      html`Value
        <vaadin-grid-filter path="Json">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100%"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>`,
      root
    );

    const filter: GridFilter = root.querySelector(
      'vaadin-grid-filter'
    ) as GridFilter;
    root
      .querySelector('vaadin-text-field')!
      .addEventListener('value-changed', (e: CustomEvent) => {
        filter.value = e.detail.value;
        this.dispatchEvent(
          new CustomEvent('searching-audit-started', {
            detail: {},
            bubbles: true,
            composed: true
          })
        );
      });
  }

  UpdatedRenderer(
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<RefDataAuditApiModel>
  ) {
    let sTime = '';
    let sDate = '';

    if (model.item.Date !== undefined) {
      const dt = new Date(model.item.Date);
      sTime = dt.toLocaleTimeString('en-GB');
      sDate = dt.toLocaleDateString('en-GB', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric'
      });
    }

    render(html` <span>${`${sDate} ${sTime}`}</span> `, root);
  }

  valueRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<RefDataAuditApiModel>
  ) {

    render(
        html` <div id="old" style="margin:0px">
            ${document.createRange().createContextualFragment(model.item.Json ?? '')}
          </div>
          <div id="new">${model.item.Json ?? ''}</div>`,
        root
    );
  }

  getProjectValuesAudit(
    params: GridDataProviderParams<RefDataAuditApiModel>,
    callback: GridDataProviderCallback<RefDataAuditApiModel>
  ) {
    if (this.project == undefined || this.project == null)
      return;
    // const valueIdx = params.filters.findIndex(
    //   filter => filter.path === 'Value'
    // );

    // if (valueIdx !== -1) {
    //   const auditToFromValue = params.filters[valueIdx].value;
    //   params.filters.splice(valueIdx, 1);
    //   if (auditToFromValue !== '') {
    //     params.filters.push({ path: 'ToValue', value: auditToFromValue });
    //     params.filters.push({ path: 'FromValue', value: auditToFromValue });
    //   }
    // }

    const pathNames = ['PropertyName', 'EnvironmentName', 'UpdatedBy'];
    pathNames.forEach(x => {
      const idIdx = params.filters.findIndex(filter => filter.path === x);
      if (idIdx !== -1) {
        const idValue = params.filters[idIdx].value;
        params.filters.splice(idIdx, 1);
        if (idValue !== '') {
          params.filters.push({ path: x, value: idValue });
        }
      }
    });

    const api = new RefDataProjectAuditApi();
    api
      .refDataProjectAuditPut({
        projectId: this.project.ProjectId,
        pagedDataOperators: {
          Filters: params.filters.map(
            (f: GridFilterDefinition): PagedDataFilter => ({
              Path: f.path,
              FilterValue: f.value
            })
          ),
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
          data.Items?.map(
            item => (item.Username = item.Username?.split('\\')[1])
          );
          this.dispatchEvent(
            new CustomEvent('searching-audit-finished', {
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
            new CustomEvent('variable-value-audit-loaded', {
              detail: {},
              bubbles: true,
              composed: true
            })
          );
          console.log(
            `done loading Project Audit page:${params.page + Number(1)}`
          );
        }
      });
    }

    public open() {
      this.dialog.open = true;
    }
}
