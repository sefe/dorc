import '@polymer/paper-dialog';
import '@vaadin/button';
import {
  GridDataProviderCallback,
  GridDataProviderParams,
  GridItemModel,
  GridSorterDefinition
} from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
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
import '../components/add-daemon';
import {
  FlatPropertyValueApiModel,
  GetScopedPropertyValuesResponseDto,
  PagedDataSorting,
  RefDataSearchPropertyValuesApi
} from '../apis/dorc-api';
import { PagedDataFilter, PropertyValueAuditApiModel } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import '../components/grid-button-groups/variable-value-controls';
import { PropertyValueDto } from '../apis/dorc-api';

@customElement('page-variables-value-lookup')
export class PageVariablesValueLookup extends PageElement {
  @property({ type: Array })
  variableValues: Array<FlatPropertyValueApiModel> = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  private nameFilterValue = '';
  private scopeFilterValue = '';
  private valueFilterValue = '';

  private _editingValueId: number | undefined;

  @property({ type: Boolean }) loading = true;

  @property({ type: Boolean }) searching = false;

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
      vaadin-grid#grid {
        overflow: hidden;
      }
      vaadin-text-field {
        padding: 0px;
        margin: 0px;
      }
      vaadin-grid-cell-content {
        padding-top: 0px;
        padding-bottom: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    return html`
      <div
        class="overlay"
        style="z-index: 1000"
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
        .dataProvider="${this.getPropertyValues}"
        style="width: 100%;"
      >
        <vaadin-grid-column
          path="Property"
          .headerRenderer="${this.nameHeaderRenderer}"
          resizable
          auto-width
        >
        </vaadin-grid-column>
        <vaadin-grid-column
          path="PropertyValueScope"
          .headerRenderer="${this.scopeHeaderRenderer}"
          resizable
          auto-width
        ></vaadin-grid-column>
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
      'searching-values-started',
      this.searchingValuesStarted as EventListener
    );

    this.addEventListener(
      'searching-values-finished',
      this.searchingValuesFinished as EventListener
    );

    this.addEventListener('values-loaded', this.valuesLoaded as EventListener);
    this.addEventListener('editing-started', ((e: CustomEvent) => {
      this._editingValueId = e.detail.id;
      (this.shadowRoot?.querySelector('vaadin-grid') as any)?.requestContentUpdate?.();
    }) as EventListener);
    this.addEventListener('editing-cancelled', (() => {
      this._editingValueId = undefined;
      (this.shadowRoot?.querySelector('vaadin-grid') as any)?.requestContentUpdate?.();
    }) as EventListener);
  }

  private searchingValuesStarted() {
    this.searching = true;
  }

  private searchingValuesFinished() {
    this.searching = false;
  }

  private valuesLoaded() {
    this.loading = false;
  }

  valueRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<FlatPropertyValueApiModel>
  ) => {
    const converted: PropertyValueDto = {
      Id: model.item.PropertyValueId,
      Value: model.item.PropertyValue,
      PropertyValueFilter: model.item.PropertyValueScope,
      PropertyValueFilterId: model.item.PropertyValueScopeId,
      UserEditable: model.item.UserEditable,
      Property: {
        Id: model.item.PropertyId,
        Name: model.item.Property,
        Secure: model.item.Secure
      }
    };

    render(
      html`<variable-value-controls
        .value="${converted}"
        .editing="${converted.Id === this._editingValueId}"
      >
      </variable-value-controls>`,
      root
    );
  };

  nameHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing-xs">
          <vaadin-grid-sorter path="Property"></vaadin-grid-sorter>
          <vaadin-text-field
            placeholder="Name"
            clear-button-visible
            focus-target
            style="width: 100px"
            theme="small"
            @input="${(e: InputEvent) => {
              const textField = e.target as HTMLInputElement;
              this.nameFilterValue = textField?.value ?? '';
              this.refreshGrid();
            }}"
          ></vaadin-text-field>
        </vaadin-horizontal-layout>
      `,
      root
    );
  }

  scopeHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing-xs">
          <vaadin-grid-sorter path="PropertyValueScope"></vaadin-grid-sorter>
          <vaadin-text-field
            placeholder="Scope"
            clear-button-visible
            focus-target
            style="width: 100px"
            theme="small"
            @input="${(e: InputEvent) => {
              const textField = e.target as HTMLInputElement;
              this.scopeFilterValue = textField?.value ?? '';
              this.refreshGrid();
            }}"
          ></vaadin-text-field>
        </vaadin-horizontal-layout>
      `,
      root
    );
  }

  valueHeaderRenderer = (root: HTMLElement) => {
    render(
      html`
        <vaadin-horizontal-layout style="align-items: center;" theme="spacing-xs">
          <vaadin-grid-sorter path="PropertyValue"></vaadin-grid-sorter>
          <vaadin-text-field
            placeholder="Value"
            clear-button-visible
            focus-target
            style="width: 100px"
            theme="small"
            @input="${(e: InputEvent) => {
              const textField = e.target as HTMLInputElement;
              this.valueFilterValue = textField?.value ?? '';
              this.refreshGrid();
            }}"
          ></vaadin-text-field>
        </vaadin-horizontal-layout>
      `,
      root
    );
  }

  private refreshGrid() {
    this.dispatchEvent(
      new CustomEvent('searching-values-started', {
        detail: {},
        bubbles: true,
        composed: true
      })
    );
    this.shadowRoot?.querySelector('vaadin-grid')?.clearCache();
  }

  getPropertyValues = (
    params: GridDataProviderParams<PropertyValueAuditApiModel>,
    callback: GridDataProviderCallback<PropertyValueAuditApiModel>
  ) => {
    const filters: PagedDataFilter[] = [];

    if (this.nameFilterValue) {
      filters.push({ Path: 'Property', FilterValue: this.nameFilterValue });
    }
    if (this.scopeFilterValue) {
      filters.push({ Path: 'PropertyValueScope', FilterValue: this.scopeFilterValue });
    }
    if (this.valueFilterValue) {
      filters.push({ Path: 'PropertyValue', FilterValue: this.valueFilterValue });
    }

    const api = new RefDataSearchPropertyValuesApi();
    api
      .refDataSearchPropertyValuesPut({
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
        next: (data: GetScopedPropertyValuesResponseDto) => {
          this.dispatchEvent(
            new CustomEvent('searching-values-finished', {
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
            new CustomEvent('values-loaded', {
              detail: {},
              bubbles: true,
              composed: true
            })
          );
          console.log(
            `done loading variables values lookup page:${
              params.page + Number(1)
            }`
          );
        }
      });
  }
}
