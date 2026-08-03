import { columnBodyRenderer, columnHeaderRenderer } from '@vaadin/grid/lit';
import '../components/dorc-spinner';
import '@vaadin/button';
import { GridDataProviderCallback, GridDataProviderParams, GridSorterDefinition } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid-sorter';
import '@vaadin/horizontal-layout';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import { css, PropertyValues } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/add-daemon';
import { FlatPropertyValueApiModel, GetScopedPropertyValuesResponseDto, PagedDataSorting, RefDataSearchPropertyValuesApi } from '../apis/dorc-api';
import { PagedDataFilter, PropertyValueAuditApiModel } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import '../components/grid-button-groups/variable-value-controls';
import { PropertyValueDto } from '../apis/dorc-api';

@customElement('page-variables-value-lookup')
export class PageVariablesValueLookup extends ResponsiveMixin(PageElement) {
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
      :host {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;
        --divider-color: var(--dorc-border-color);
      }
      vaadin-grid#grid {
        flex: 1;
        min-height: 0;
      }
      .highlight {
        background-color: var(--dorc-chip-bg);
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
      @media (max-width: 768px) {
        vaadin-grid-cell-content {
          white-space: normal;
          word-wrap: break-word;
          overflow-wrap: break-word;
        }
      }
    `;
  }

  render() {
    return html`
      <dorc-spinner style="--dorc-spinner-z-index: 1000" ?hidden="${!(this.loading || this.searching)}"></dorc-spinner>
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
          ${columnHeaderRenderer(this.nameHeaderRenderer, [])}
          resizable
          auto-width
        >
        </vaadin-grid-column>
        <vaadin-grid-column
          path="PropertyValueScope"
          ${columnHeaderRenderer(this.scopeHeaderRenderer, [])}
          resizable
          auto-width
          ?hidden="${this._narrowScreen}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="Value"
          ${columnBodyRenderer(this.valueRenderer, [])}
          ${columnHeaderRenderer(this.valueHeaderRenderer, [])}
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
    item: FlatPropertyValueApiModel
  ) => {
    const converted: PropertyValueDto = {
      Id: item.PropertyValueId,
      Value: item.PropertyValue,
      PropertyValueFilter: item.PropertyValueScope,
      PropertyValueFilterId: item.PropertyValueScopeId,
      UserEditable: item.UserEditable,
      Property: {
        Id: item.PropertyId,
        Name: item.Property,
        Secure: item.Secure
      }
    };

    return html`<variable-value-controls
        .value="${converted}"
        .editing="${converted.Id === this._editingValueId}"
      >
      </variable-value-controls>`;
  };

  nameHeaderRenderer = () => {
    return html`
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
      `;
  }

  scopeHeaderRenderer = () => {
    return html`
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
      `;
  }

  valueHeaderRenderer = () => {
    return html`
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
      `;
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
