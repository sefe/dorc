import { css, PropertyValues, render } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import {
  GridDataProviderCallback,
  GridDataProviderParams,
  GridFilterDefinition,
  GridSorterDefinition
} from '@vaadin/grid/vaadin-grid';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-filter';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { Grid, GridItemModel } from '@vaadin/grid';
import '../grid-button-groups/variable-value-controls';
import '../dismissible-item';
import { ComboBox, ComboBoxRenderer } from '@vaadin/combo-box';
import { TextField } from '@vaadin/text-field';
import { Checkbox } from '@vaadin/checkbox';
import {
  PropertiesApi,
  PropertyApiModel,
  PropertyValueDto,
  PropertyValuesApi,
  PropertyValueScopeOptionApiModel,
  Response
} from '../../apis/dorc-api';
import {
  EnvironmentApiModel,
  FlatPropertyValueApiModel,
  GetScopedPropertyValuesResponseDto,
  PagedDataFilter,
  PagedDataSorting,
  RefDataScopedPropertyValuesApi
} from '../../apis/dorc-api';
import { PageEnvBase } from './page-env-base';
import { ErrorNotification } from '../notifications/error-notification';

const variableValue = 'PropertyValue';
const variableName = 'Property';
const variableSecure = 'PropertyValueScope';

let _environment: EnvironmentApiModel | undefined;
@customElement('env-variables')
export class EnvVariables extends PageEnvBase {
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

  variableValue: string = '';
  variableName: string = '';
  variableSecure: boolean = false;

  static get styles() {
    return css`
      :host {
        display: flex;
        width: 100%;
        overflow: hidden;
        height: 100%;
      }
      vaadin-grid#grid {
        --divider-color: rgb(223, 232, 239);
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
        top: 30%;
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
      ${this.envLoaded
        ? html`
            <vaadin-vertical-layout style="width: 100%; height: 100%">
              <vaadin-details
                id="details"
                opened
                summary="Add Scoped Variable Value"
                style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; width: 100%; margin: 0px;"
              >
                <div
                  style="display: flex; flex-wrap: wrap; flex-direction: row"
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
                          id="properties"
                          @value-changed="${this._propNameValueChanged}"
                          .items="${this.properties}"
                          label="Existing Variable Name"
                          placeholder="Select Variable Name"
                          clear-button-visible
                          item-label-path="Name"
                          item-value-path="Name"
                          style="min-width: 600px; margin-left: 5px; "
                        ></vaadin-combo-box>
                      </td>
                    </tr>
                  </table>
                  <table>
                    <tr>
                      <td style="vertical-align: center; min-width: 20px">
                        ${this.loadingScopeOptions
                          ? html`<div
                              style="vertical-align: center"
                              class="small-loader"
                            ></div> `
                          : html``}
                      </td>
                      <td style="vertical-align: top;">
                        <vaadin-combo-box
                          allow-custom-value
                          .items="${this.propertyValueScopeOptions}"
                          item-label-path="ValueOption"
                          item-value-path="ValueOption"
                          .renderer="${this.comboboxRenderer}"
                          id="newVariableValue"
                          label="Value"
                          style="min-width: 400px; "
                          helper-text="Include a resolver eg. $AnotherVariable$ or specify value directly"
                        ></vaadin-combo-box>
                      </td>
                      <td style="vertical-align: center;">
                        <vaadin-button
                          @click="${this._addVariableValueClick}"
                          ?disabled="${!this.environment?.UserEditable}"
                          >Add Variable Value</vaadin-button
                        >
                      </td>
                      <td style="vertical-align: center; min-width: 20px">
                        ${this.addingVariableValue
                          ? html`<div
                              style="vertical-align: center"
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
                    this.variableValue !== '' &&
                    this.variableValue !== undefined
                  ) {
                    params.filters.push({
                      path: variableValue,
                      value: this.variableValue
                    });
                  }

                  if (
                    this.variableName !== '' &&
                    this.variableName !== undefined
                  ) {
                    params.filters.push({
                      path: variableName,
                      value: this.variableName
                    });
                  }

                  if (this.variableSecure) {
                    params.filters.push({
                      path: variableSecure,
                      value: _environment?.EnvironmentName ?? ''
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
                  .headerRenderer="${this.nameHeaderRenderer}"
                >
                </vaadin-grid-column>
                <vaadin-grid-column
                  path="PropertyValueScope"
                  header="Variable Scope"
                  .headerRenderer="${this.scopeHeaderRenderer}"
                  resizable
                  auto-width
                  flex-grow="0"
                ></vaadin-grid-column>
                <vaadin-grid-column
                  path="Secure"
                  resizable
                  auto-width
                  text-align="center"
                  .renderer="${this.secureRenderer}"
                  .headerRenderer="${this.secureHeaderRenderer}"
                  flex-grow="0"
                >
                </vaadin-grid-column>
                <vaadin-grid-column
                  header="Variable Value"
                  .headerRenderer="${this.valueHeaderRenderer}"
                  .renderer="${this.variableValueControlsRenderer}"
                  resizable
                  flex-grow="0"
                  width="60rem"
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

    this.getAllVariableNames();
  }

  private searchingEnvVariablesStarted(event: CustomEvent) {
    if (event.detail.value !== undefined) {
      this.debouncedInputHandler(event.detail.field, event.detail.value);
    }
  }

  private debouncedInputHandler = this.debounce(
    (field: string, value: string) => {
      switch (field) {
        case variableValue:
          this.variableValue = value;
          break;
        case variableName:
          this.variableName = value;
          break;
        case variableSecure:
          this.variableSecure = !this.variableSecure;
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

  private comboboxRenderer: ComboBoxRenderer<PropertyValueScopeOptionApiModel> =
    (root, _, { item: scopeOption }) => {
      const exampleOption = JSON.stringify(scopeOption.SampleResolvedValue);

      render(
        html`
          <div style="display: flex;">
            <div>
              ${scopeOption.ValueOption}
              <div
                style="font-size: var(--lumo-font-size-s); color: var(--lumo-secondary-text-color);"
              >
                ${exampleOption}
              </div>
            </div>
          </div>
        `,
        root
      );
    };

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
            } else {
              this.errorAlert(value);
              this.addingVariableValue = false;
            }
          },
          error: (err: any) => {
            this.errorAlert(err);
            this.addingVariableValue = false;
          },
          complete: () => console.log('done adding variable value')
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
      let msg = '';
      msg = this.processError(element);
      if (msg !== '') {
        const notification = new ErrorNotification();
        notification.setAttribute('errorMessage', msg);
        this.shadowRoot?.appendChild(notification);
        notification.open();
      }
    });
  }

  variableValueControlsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<FlatPropertyValueApiModel>
  ) {
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
      html`<variable-value-controls .value="${converted}">
      </variable-value-controls>`,
      root
    );
  }

  secureRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<FlatPropertyValueApiModel>
  ) {
    const checkbox = new Checkbox();

    checkbox.checked = model.item.Secure ?? false;
    checkbox.disabled = true;
    render(checkbox, root);
  }

  constructor() {
    super();
    super.loadEnvironmentInfo();
  }

  notifyEnvironmentReady() {
    _environment = this.environment;
  }

  nameHeaderRenderer(root: HTMLElement) {
    render(
      html`
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
      `,
      root
    );
  }

  valueHeaderRenderer(root: HTMLElement) {
    render(
      html`
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
      `,
      root
    );
  }

  secureHeaderRenderer(root: HTMLElement) {
    render(
      html`
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
      `,
      root
    );
  }

  scopeHeaderRenderer(root: HTMLElement) {
    render(
      html`
        <table>
          <tr>
            <td>
              <vaadin-grid-sorter
                path="PropertyValueScope"
                style="align-items: normal"
              ></vaadin-grid-sorter>
            </td>
              <td>
                  <vaadin-checkbox slot='filter' style="font-size: var(--lumo-font-size-s)"
                                   theme="small"
                                   ?checked="${!_environment?.EnvironmentSecure}"
                                   @change="${(e: any) => {
                                     this.dispatchEvent(
                                       new CustomEvent(
                                         'searching-env-variables-started',
                                         {
                                           detail: {
                                             field: variableSecure,
                                             value: e.target.checked
                                           },
                                           bubbles: true,
                                           composed: true
                                         }
                                       )
                                     );
                                   }}"
                  ><label slot="label" title='Show default property values also'
                  >Show Defaults</vaadin-checkbox
                  ></td>
          </tr>
        </table>
          </tr>
        </table>
      `,
      root
    );
  }
}
