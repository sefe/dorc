import '@vaadin/checkbox';
import { comboBoxRenderer } from '@vaadin/combo-box/lit';
import { columnBodyRenderer, columnHeaderRenderer } from '@vaadin/grid/lit';
import { css, PropertyValues } from 'lit';
import '../dorc-spinner';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { GridDataProviderCallback, GridDataProviderParams, GridFilterDefinition, GridSorterDefinition } from '@vaadin/grid/vaadin-grid';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-filter';
import { Grid } from '@vaadin/grid';
import '../grid-button-groups/variable-value-controls';
import '../dismissible-item';
import { ComboBox } from '@vaadin/combo-box';
import { TextField } from '@vaadin/text-field';
import { PropertiesApi, PropertyApiModel, PropertyValueDto, PropertyValuesApi, PropertyValueScopeOptionApiModel, Response } from '../../apis/dorc-api';
import { EnvironmentApiModel, FlatPropertyValueApiModel, GetScopedPropertyValuesResponseDto, PagedDataFilter, PagedDataSorting, RefDataScopedPropertyValuesApi } from '../../apis/dorc-api';
import { PageEnvBase } from './page-env-base';
import { ResponsiveMixin } from '../../helpers/responsive-mixin';
import { ErrorNotification } from '../notifications/error-notification';
import { Notification } from '@vaadin/notification';
import '@vaadin/grid/vaadin-grid-sorter';

const variableValue = 'PropertyValue';
const variableName = 'Property';
const variableScope = 'PropertyValueScope';
const variableIsShowDefaultProps = 'ShowDefaults';

let _environment: EnvironmentApiModel | undefined;
@customElement('env-variables')
export class EnvVariables extends ResponsiveMixin(PageEnvBase) {
  private secureMessage =
    'This environment is not secure which includes default variables during deployments';

  @property({ type: Boolean }) loadingProperties = true;

  @property({ type: Boolean }) loadingScopeOptions = true;

  @property({ type: Boolean }) addingVariableValue = false;

  @property({ type: Array }) properties: PropertyApiModel[] | undefined;

  @property({ type: Array })
  propertyValueScopeOptions!: PropertyValueScopeOptionApiModel[];

  @query('#grid') grid: Grid | undefined;

  @property({ type: Boolean }) loading = true;

  @property({ type: Boolean }) searching = false;

  private propertyName = '';

  filterVariableValue: string = '';
  filterVariableName: string = '';
  filterVariableScope: string = '';
  isShowDefaultProps: boolean = false;

  @state() private _editingValueId: number | undefined;

  static get styles() {
    return css`
      :host {
        display: flex;
        width: 100%;
        overflow: hidden;
        height: 100%;
      }
      vaadin-grid#grid {
        --divider-color: var(--dorc-border-color);
        width: 100%;
        height: 100%;
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
      .small-loader {
        border: 2px solid #f3f3f3; /* Light grey */
        border-top: 2px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
      }
      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }
      vaadin-combo-box {
        padding: 0px;
      }

      .env-variable-selector-combo {
        width: clamp(24rem, 34vw, 36rem);
        min-width: 24rem;
        max-width: none;
        margin-left: var(--lumo-space-xs);
      }
      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }
      @media (max-width: 768px) {
        .env-variable-selector-combo {
          width: 100%;
          min-width: 0;
          margin-left: 0;
        }

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
      ${this.envLoaded
        ? html`
            <vaadin-vertical-layout style="width: 100%; height: 100%">
              <vaadin-details
                id="details"
                opened
                summary="Add Scoped Variable Value"
                style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; width: 100%; margin: 0px;"
              >
                <div
                  style="display: flex; flex-wrap: wrap; flex-direction: row; width: 100%"
                >
                  <table>
                    <tr>
                      <td style="vertical-align: center; min-width: 20px">
                        ${this.loadingProperties
                          ? html`<div
                              style="vertical-align: center"
                              class="small-loader"
                            ></div> `
                          : html``}
                      </td>
                      <td style="vertical-align: top;">
                        <vaadin-combo-box
                          class="env-variable-selector-combo"
                          id="properties"
                          @value-changed="${this._propNameValueChanged}"
                          .items="${this.properties}"
                          label="Existing Variable Name"
                          placeholder="Select Variable Name"
                          clear-button-visible
                          item-label-path="Name"
                          item-value-path="Name"
                        ></vaadin-combo-box>
                      </td>
                    </tr>
                  </table>
                  <table style="flex: 1; min-width: 400px">
                    <tr>
                      <td style="vertical-align: center; min-width: 20px">
                        ${this.loadingScopeOptions
                          ? html`<div
                              style="vertical-align: center"
                              class="small-loader"
                            ></div> `
                          : html``}
                      </td>
                      <td style="vertical-align: top; width: 100%;">
                        <vaadin-combo-box
                          allow-custom-value
                          .items="${this.propertyValueScopeOptions}"
                          item-label-path="ValueOption"
                          item-value-path="ValueOption"
                          ${comboBoxRenderer(this.comboboxRenderer, [])}
                          id="newVariableValue"
                          label="Value"
                          style="min-width: 400px; width: 100%"
                          helper-text="Include a resolver eg. $AnotherVariable$ or specify value directly"
                        ></vaadin-combo-box>
                      </td>
                      <td style="vertical-align: middle;">
                        <vaadin-button
                          @click="${this._addVariableValueClick}"
                          ?disabled="${!this.environment?.UserEditable}"
                          >Add Variable Value</vaadin-button
                        >
                      </td>
                      <td style="vertical-align: middle; min-width: 20px">
                        ${this.addingVariableValue
                          ? html`<div
                              style="vertical-align: middle"
                              class="small-loader"
                            ></div> `
                          : html``}
                      </td>
                    </tr>
                  </table>
                </div>
              </vaadin-details>

              ${!this.environment?.EnvironmentSecure
                ? html`<dismissible-item
                    style="flex: 0 1 auto; width: 100%;"
                    .message="${this.secureMessage}"
                  ></dismissible-item>`
                : html``}
              <vaadin-grid
                id="grid"
                column-reordering-allowed
                multi-sort
                theme="compact row-stripes no-row-borders no-border"
                .dataProvider="${(
                  params: GridDataProviderParams<FlatPropertyValueApiModel>,
                  callback: GridDataProviderCallback<FlatPropertyValueApiModel>
                ) => {
                  if (
                    this.filterVariableValue !== '' &&
                    this.filterVariableValue !== undefined
                  ) {
                    params.filters.push({
                      path: variableValue,
                      value: this.filterVariableValue
                    });
                  }

                  if (
                    this.filterVariableName !== '' &&
                    this.filterVariableName !== undefined
                  ) {
                    params.filters.push({
                      path: variableName,
                      value: this.filterVariableName
                    });
                  }

                  if (
                    this.filterVariableScope !== '' &&
                    this.filterVariableScope !== undefined
                  ) {
                    params.filters.push({
                      path: variableScope,
                      value: this.filterVariableScope
                    });
                  }

                  if (this.isShowDefaultProps && _environment?.EnvironmentName) {
                    params.filters.push({
                      path: variableScope,
                      value: _environment.EnvironmentName
                    });
                  }

                  if (_environment && _environment?.EnvironmentName !== '') {
                    const api = new RefDataScopedPropertyValuesApi();
                    api
                      .refDataScopedPropertyValuesPut({
                        pagedDataOperators: {
                          Filters: params.filters.map(
                            (f: GridFilterDefinition): PagedDataFilter => ({
                              Path: f.path,
                              FilterValue: String(f.value ?? '')
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
                        scope: _environment?.EnvironmentName || ' '
                      })
                      .subscribe({
                        next: (data: GetScopedPropertyValuesResponseDto) => {
                          this.dispatchEvent(
                            new CustomEvent(
                              'searching-env-variables-finished',
                              {
                                detail: {},
                                bubbles: true,
                                composed: true
                              }
                            )
                          );
                          callback(data.Items ?? [], data.TotalItems);
                        },
                        error: (err: any) => console.error(err),
                        complete: () => {
                          this.dispatchEvent(
                            new CustomEvent('env-variables-loaded', {
                              detail: {},
                              bubbles: true,
                              composed: true
                            })
                          );
                          console.log(
                            `done loading scoped Property Values page:${params.page}`
                          );
                        }
                      });
                  }
                }}"
                ?hidden="${this.loading}"
                style="z-index: 100;"
              >
                <vaadin-grid-column
                  path="Property"
                  header="Variable Name"
                  resizable
                  flex-grow="0"
                  width="20rem"
                  ${columnHeaderRenderer(this.nameHeaderRenderer, [])}
                >
                </vaadin-grid-column>
                <vaadin-grid-column
                  path="PropertyValueScope"
                  header="Variable Scope"
                  ${columnHeaderRenderer(this.scopeHeaderRenderer, [])}
                  resizable
                  auto-width
                  flex-grow="0"
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-column>
                <vaadin-grid-column
                  path="Secure"
                  resizable
                  auto-width
                  text-align="center"
                  ${columnBodyRenderer(this.secureRenderer, [])}
                  ${columnHeaderRenderer(this.secureHeaderRenderer, [])}
                  flex-grow="0"
                  ?hidden="${this._narrowScreen}"
                >
                </vaadin-grid-column>
                <vaadin-grid-column
                  header="Variable Value"
                  ${columnHeaderRenderer(this.valueHeaderRenderer, [])}
                  ${columnBodyRenderer(this.variableValueControlsRenderer, [
                    this._editingValueId
                  ])}
                  resizable
                  flex-grow="1"
                  width="20rem"
                ></vaadin-grid-column>
              </vaadin-grid>
            </vaadin-vertical-layout>
          `
        : html``}
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'env-variables-loaded',
      this.variablesLoaded as EventListener
    );
    this.addEventListener(
      'searching-env-variables-started',
      this.searchingEnvVariablesStarted as EventListener
    );
    this.addEventListener(
      'searching-env-variables-finished',
      this.searchingEnvVariablesFinished as EventListener
    );
    this.addEventListener(
      'variable-value-deleted',
      this.variableValueDeleted as EventListener
    );
    this.addEventListener('editing-started', ((e: CustomEvent) => {
      this._editingValueId = e.detail.id;
    }) as EventListener);
    this.addEventListener('editing-cancelled', (() => {
      this._editingValueId = undefined;
    }) as EventListener);

    this.getAllVariableNames();
  }

  private searchingEnvVariablesStarted(event: CustomEvent) {
    if (event.detail.value !== undefined) {
      this.debouncedInputHandler(event.detail.field, event.detail.value);
    }
  }

  private debouncedInputHandler = this.debounce(
    (field: string, value: string | boolean) => {
      switch (field) {
        case variableValue:
          this.filterVariableValue = value as string;
          break;
        case variableName:
          this.filterVariableName = value as string;
          break;
        case variableScope:
          this.filterVariableScope = value as string;
          break;
        case variableIsShowDefaultProps:
          this.isShowDefaultProps = !(value as boolean);
          break;
        default:
          break;
      }
      this.grid?.clearCache();
      this.searching = true;
    },
    400 // debounce wait time
  );

  private searchingEnvVariablesFinished() {
    this.searching = false;
  }

  private variablesLoaded() {
    this.loading = false;
    const grid = this.grid;
    if (grid?.shadowRoot && !grid.shadowRoot.querySelector('#scrollbar-fix')) {
      const style = document.createElement('style');
      style.id = 'scrollbar-fix';
      style.textContent = '#items { margin-bottom: 1rem; }';
      grid.shadowRoot.appendChild(style);
    }
  }

  variableValueDeleted() {
    if (this.grid) {
      this.grid.clearCache();
      this.loading = true;
    }
  }

  private debounce(func: (...args: any[]) => void, wait: number) {
    let timeout: number | undefined;
    return function executedFunction(...args: any[]) {
      const later = () => {
        clearTimeout(timeout);
        func(...args);
      };
      clearTimeout(timeout);
      timeout = window.setTimeout(later, wait);
    };
  }

  private getAllVariableNames() {
    const propertiesApi = new PropertiesApi();
    propertiesApi.propertiesGet().subscribe({
      next: (data: PropertyApiModel[]) => {
        this.properties = data.sort(this.sortProperties);
        this.loadingProperties = false;
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading properties')
    });

    const api = new PropertyValuesApi();
    api
      .propertyValuesScopeOptionsGet({
        propertyValueScope: this.environmentName
      })
      .subscribe({
        next: (value: PropertyValueScopeOptionApiModel[]) => {
          this.propertyValueScopeOptions = value.sort((a, b) => {
            if (String(a.ValueOption) > String(b.ValueOption)) return 1;
            return -1;
          });
          this.loadingScopeOptions = false;
        },
        error: (err: any) => this.errorAlert(err),
        complete: () => console.log('done loading variable value scope options')
      });
  }

  sortProperties(a: PropertyApiModel, b: PropertyApiModel): number {
    if (String(a.Name) > String(b.Name)) return 1;
    return -1;
  }

  private comboboxRenderer = (
    scopeOption: PropertyValueScopeOptionApiModel
  ) => html`
    <div style="display: flex;">
      <div>
        ${scopeOption.ValueOption}
        <div
          style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
        >
          ${JSON.stringify(scopeOption.SampleResolvedValue)}
        </div>
      </div>
    </div>
  `;

  _propNameValueChanged(data: CustomEvent) {
    if (data) {
      const combo = data.target as ComboBox;
      this.propertyName = combo.value;
    }
  }

  _addVariableValueClick() {
    const textField = this.shadowRoot?.querySelector(
      '#newVariableValue'
    ) as unknown as TextField;
    this.addingVariableValue = true;
    const api = new PropertyValuesApi();
    const existingProperty = this.properties?.find(
      value => value.Name === this.propertyName
    );
    if (existingProperty) {
      const propertyValueDto: PropertyValueDto = {
        Property: existingProperty,
        Value: textField.value,
        PropertyValueFilter: this.environmentName
      };
      api
        .propertyValuesPost({ propertyValueDto: [propertyValueDto] })
        .subscribe({
          next: (value: Response[]) => {
            if (value[0].Status === 'success') {
              value.forEach((response: Response) => {
                console.log(response.Status);
              });
              this.grid?.clearCache();
              this.getAllVariableNames();
              this.addingVariableValue = false;
              this.showSuccessMessage('Variable value added successfully!');
            } else {
              this.errorAlert(value);
              this.addingVariableValue = false;
            }
          },
          error: (err: any) => {
            this.errorAlert(err);
            this.addingVariableValue = false;
          },
          complete: () => {
            console.log('done adding variable value');
          }
        });
    }
  }

  processError(element: Response): string {
    let msg = '';
    const scope = element.Item as PropertyValueDto;
    if (element.Status !== 'success') {
      if (scope.Id !== undefined) {
        let isDefault = false;
        if (
          scope.PropertyValueFilter === '' ||
          scope.PropertyValueFilter === undefined ||
          scope.PropertyValueFilter === null
        ) {
          isDefault = true;
        }
        const scopeStr = isDefault ? 'default' : scope.PropertyValueFilter;

        msg = `${scope.Property?.Name} - Scope '${scopeStr}' - ${
          element.Status
        }`;
      } else {
        msg = `${element.Item} - ${element.Status}`;
      }
    }
    return msg;
  }

  errorAlert(errs: Response[]) {
    console.error(errs);

    errs.forEach(element => {
      const msg = this.processError(element);
      if (msg !== '') {
        const notification = new ErrorNotification();
        notification.setAttribute('errorMessage', msg);
        this.shadowRoot?.appendChild(notification);
        notification.open();
      }
    });
  }

  variableValueControlsRenderer = (
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

  secureRenderer(item: FlatPropertyValueApiModel) {
    return html`<vaadin-checkbox
      disabled
      .checked="${item.Secure ?? false}"
    ></vaadin-checkbox>`;
  }

  constructor() {
    super();
    super.loadEnvironmentInfo();
  }

  notifyEnvironmentReady() {
    _environment = this.environment;
  }

  nameHeaderRenderer() {
    return html`
        <vaadin-grid-sorter
          path="Property"
          direction="asc"
          style="align-items: normal"
        ></vaadin-grid-sorter>
        <vaadin-text-field
          placeholder="Name"
          clear-button-visible
          focus-target
          style="width: 100px"
          theme="small"
          @input="${(e: InputEvent) => {
            const textField = e.target as TextField;

            this.dispatchEvent(
              new CustomEvent('searching-env-variables-started', {
                detail: {
                  field: variableName,
                  value: textField?.value
                },
                bubbles: true,
                composed: true
              })
            );
          }}"
        ></vaadin-text-field>
      `;
  }

  valueHeaderRenderer() {
    return html`
        <vaadin-text-field
          placeholder="Value"
          clear-button-visible
          focus-target
          style="width: 100px"
          theme="small"
          @input="${(e: InputEvent) => {
            const textField = e.target as TextField;

            this.dispatchEvent(
              new CustomEvent('searching-env-variables-started', {
                detail: {
                  field: variableValue,
                  value: textField?.value
                },
                bubbles: true,
                composed: true
              })
            );
          }}"
        ></vaadin-text-field>
      `;
  }

  secureHeaderRenderer() {
    return html`
        <table>
          <tr>
            <td>
              <vaadin-grid-sorter
                path="Secure"
                style="align-items: normal"
              ></vaadin-grid-sorter>
            </td>
            <td>
              <div style="padding: 2px; display: flex; align-items: center;">
                Secure
              </div>
            </td>
          </tr>
        </table>
      `;
  }

  scopeHeaderRenderer() {
    return html`
        <table>
          <tr>
            <td>
              <vaadin-grid-sorter
                path="PropertyValueScope"
                style="align-items: normal"
              ></vaadin-grid-sorter>
            </td>
            <td>
              <vaadin-text-field
                clear-button-visible
                placeholder="Scope"
                focus-target
                style="width: 100px"
                theme="small"
                @input="${(e: InputEvent) => {
                  const textField = e.target as TextField;

                  this.dispatchEvent(
                    new CustomEvent('searching-env-variables-started', {
                      detail: {
                        field: variableScope,
                        value: textField?.value
                      },
                      bubbles: true,
                      composed: true
                    })
                  );
                }}"
              ></vaadin-text-field>
            </td>
            <td>
              <vaadin-checkbox 
                style="font-size: var(--lumo-font-size-s)"
                theme="small"
                ?checked="${!_environment?.EnvironmentSecure}"
                @change="${(e: any) => {
                  this.dispatchEvent(
                    new CustomEvent(
                      'searching-env-variables-started',
                      {
                        detail: {
                          field: variableIsShowDefaultProps,
                          value: e.target.checked
                        },
                        bubbles: true,
                        composed: true
                      }
                    )
                  );
                }}"
              >
                <label slot="label" title='Show default property values also'
                >Show Defaults</label>
              </vaadin-checkbox>
            </td>
          </tr>
        </table>
      `;
  }

  private showSuccessMessage(text: string) {
    Notification.show(text, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }
}