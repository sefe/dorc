import '@polymer/paper-dialog';
import '@vaadin/button';
import {
  GridDataProviderCallback,
  GridDataProviderParams,
  GridFilterDefinition,
  GridItemModel,
  GridSorterDefinition
} from '@vaadin/grid';
import '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-filter';
import { GridFilter } from '@vaadin/grid/vaadin-grid-filter';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import '@vaadin/checkbox';
import { css, PropertyValues, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/add-daemon';
import { PagedDataSorting, PropertyValuesAuditApi } from '../apis/dorc-api';
import {
  GetPropertyValuesAuditListResponseDto,
  PagedDataFilter,
  PropertyValueAuditApiModel
} from '../apis/dorc-api/models';
import { PageElement } from '../helpers/page-element';
import { getShortLogonName } from '../helpers/user-extensions';

@customElement('page-variables-audit')
export class PageVariablesAudit extends PageElement {
  @property({ type: Array }) scripts: Array<PropertyValueAuditApiModel> = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property() nameFilterValue = '';

  @property({ type: Boolean }) loading = true;

  @property({ type: Boolean }) searching = false;

  @property({ type: Boolean }) useAndFilter = true;

  static get styles() {
    return css`
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 96px);
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
        .dataProvider="${this.getPropertyValuesAudit}"
        .cellClassNameGenerator="${this.cellClassNameGenerator}"
        style="width: 100%; z-index: 1"
        ?hidden="${this.loading}"
      >
        <vaadin-grid-column
          path="PropertyName"
          header="Property Name"
          resizable
          .headerRenderer="${this.nameHeaderRenderer}"
          auto-width
        >
        </vaadin-grid-column>
        <vaadin-grid-column
          path="EnvironmentName"
          header="Environment"
          .headerRenderer="${this.environmentHeaderRenderer}"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="UpdatedBy"
          header="User"
          .headerRenderer="${this.userHeaderRenderer}"
          resizable
          auto-width
        ></vaadin-grid-column>
        <vaadin-grid-sort-column
          path="UpdatedDate"
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
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'variable-value-audit-loaded',
      this.variableValuesAuditLoaded as EventListener
    );
    this.addEventListener(
      'searching-audit-started',
      this.searchingAuditsStarted as EventListener
    );
    this.addEventListener(
      'searching-audit-finished',
      this.searchingAuditsFinished as EventListener
    );
  }

  private searchingAuditsStarted() {
    this.searching = true;
  }

  private searchingAuditsFinished() {
    this.searching = false;
  }

  private variableValuesAuditLoaded() {
    this.loading = false;
  }

  UpdatedRenderer(
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<PropertyValueAuditApiModel>
  ) {
    let sTime = '';
    let sDate = '';

    if (model.item.UpdatedDate !== undefined) {
      const dt = new Date(model.item.UpdatedDate);
      sTime = dt.toLocaleTimeString('en-GB');
      sDate = dt.toLocaleDateString('en-GB', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric'
      });
    }

    render(html` <span>${`${sDate} ${sTime}`}</span> `, root);
  }

  private cellClassNameGenerator(
    _: GridColumn,
    model: GridItemModel<PropertyValueAuditApiModel>
  ) {
    const { item } = model;
    let classes = '';

    if (item.Type === 'Insert') {
      classes += ' insert-type';
    }

    if (item.Type === 'Delete') {
      classes += ' delete-type';
    }
    return classes;
  }

  valueRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<PropertyValueAuditApiModel>
  ) {
    const oldText = model.item.FromValue;
    let text = '';
    let spanOpen = false;

    model.item.ToValue?.split('').forEach((val, i) => {
      if (val !== oldText?.charAt(i)) {
        text += !spanOpen ? "<span class='highlight'>" : '';
        spanOpen = true;
      } else {
        text += spanOpen ? '</span>' : '';
        spanOpen = false;
      }
      text += val;
    });

    const displayFromValue = model.item.FromValue?.replace(' ', '&nbsp;');

    if (text.includes('highlight')) {
      render(
        html` <div id="old" style="margin:0px">
            ${document
              .createRange()
              .createContextualFragment(displayFromValue ?? '')}
          </div>
          <div id="new">
            ${document.createRange().createContextualFragment(text)}
          </div>`,
        root
      );
    } else {
      text = '';
      spanOpen = false;
      const newText = model.item.ToValue;

      model.item.FromValue?.split('').forEach((val, i) => {
        if (val !== newText?.charAt(i)) {
          text += !spanOpen ? "<span class='highlight-removed'>" : '';
          spanOpen = true;
        } else {
          text += spanOpen ? '</span>' : '';
          spanOpen = false;
        }
        text += val;
      });

      render(
        html` <div id="old" style="margin:0px">
            ${document.createRange().createContextualFragment(text)}
          </div>
          <div id="new">${model.item.ToValue ?? ''}</div>`,
        root
      );
    }
  }

  nameHeaderRenderer(root: HTMLElement) {
    render(
      html`<vaadin-grid-sorter path="PropertyName">Name</vaadin-grid-sorter>
        <vaadin-grid-filter path="PropertyName">
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

  environmentHeaderRenderer(root: HTMLElement) {
    render(
      html`<vaadin-grid-sorter path="EnvironmentName"
          >Environment</vaadin-grid-sorter
        >
        <vaadin-grid-filter path="EnvironmentName">
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

  userHeaderRenderer(root: HTMLElement) {
    render(
      html`<vaadin-grid-sorter path="UpdatedBy">User</vaadin-grid-sorter>
        <vaadin-grid-filter path="UpdatedBy">
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

valueHeaderRenderer = (root: HTMLElement) => {
  const labelText = this.useAndFilter ? 'Search Filter: AND' : 'Search Filter: OR';
  
  render(
    html`Value
    <div>
      <vaadin-grid-filter path="Value">
        <vaadin-text-field
          clear-button-visible
          slot="filter"
          focus-target
          style="width: 100%"
          theme="small"
        ></vaadin-text-field>
      </vaadin-grid-filter>
      <vaadin-checkbox
        id="filter-checkbox"
        .checked="${!this.useAndFilter}"
        .label="${labelText}"
        title="Toggle between AND/OR filter logic"
        style="--vaadin-checkbox-size: 14px;"
        @change="${(e: Event) => {
          this.useAndFilter = !(e.target as HTMLInputElement).checked;
          const checkbox = root.querySelector('#filter-checkbox') as any;
          if (checkbox) {
            checkbox.label = this.useAndFilter ? 'Search Filter: AND' : 'Search Filter: OR';
          }
          this.shadowRoot?.querySelector('vaadin-grid')?.clearCache();
          this.dispatchEvent(
            new CustomEvent('searching-audit-started', {
              detail: {},
              bubbles: true,
              composed: true
            })
          );
        }}"
      >
      </vaadin-checkbox>
    </div>`,
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
};

getPropertyValuesAudit = (
    params: GridDataProviderParams<PropertyValueAuditApiModel>,
    callback: GridDataProviderCallback<PropertyValueAuditApiModel>,
  ) => {
    const valueIdx = params.filters.findIndex(
      filter => filter.path === 'Value'
    );

    if (valueIdx !== -1) {
      const auditToFromValue = params.filters[valueIdx].value;
      params.filters.splice(valueIdx, 1);
      if (auditToFromValue !== '') {
        params.filters.push({ path: 'ToValue', value: auditToFromValue });
        params.filters.push({ path: 'FromValue', value: auditToFromValue });
      }
    }

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

    const api = new PropertyValuesAuditApi();
    api
      .propertyValuesAuditPut({
        pagedDataOperators: {
          Filters: params.filters.map(
            (f: GridFilterDefinition): PagedDataFilter => ({
              Path: f.path,
              FilterValue: f.value,
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
        page: params.page + 1,
        useAndLogic: this.useAndFilter
      })
      .subscribe({
        next: (data: GetPropertyValuesAuditListResponseDto) => {
          data.Items?.map(
            item => (item.UpdatedBy = getShortLogonName(item.UpdatedBy))
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
            `done loading Property values Audit page:${params.page + Number(1)}`
          );
        }
      });
  }
}
