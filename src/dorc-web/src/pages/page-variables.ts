import '@vaadin/button';
import { Checkbox } from '@vaadin/checkbox';
import '@vaadin/combo-box';
import '@vaadin/checkbox';
import { ComboBox, ComboBoxRenderer } from '@vaadin/combo-box';
import '@vaadin/details';
import '@vaadin/grid';
import { GridItemModel } from '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column.js';
import '@vaadin/grid/vaadin-grid-filter';
import { GridFilter } from '@vaadin/grid/vaadin-grid-filter';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/item';
import '@vaadin/list-box';
import { Notification } from '@vaadin/notification';
import '@vaadin/radio-group';
import '@vaadin/text-area';
import '@vaadin/text-field';
import { TextField } from '@vaadin/text-field';
import { css, PropertyValueMap, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/grid-button-groups/variable-value-controls';
import { ErrorNotification } from '../components/notifications/error-notification';
import { WarningNotification } from '../components/notifications/warning-notification';
import {
  PropertiesApi,
  PropertyApiModel,
  PropertyValueDto,
  PropertyValuesApi,
  PropertyValueScopeOptionApiModel,
  Response
} from '../apis/dorc-api';
import { RefDataEnvironmentsApi } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { PropertyValueDtoExtended } from '../components/model-extensions/PropertyValueDtoExtended';
import GlobalCache from '../global-cache';
import '@vaadin/icons';
import {Router} from "@vaadin/router";

@customElement('page-variables')
export class PageVariables extends PageElement {
  newVariableName = '';

  @property({ type: Boolean }) creatingVariable = false;

  @property({ type: Boolean }) deletingVariable = false;

  @property({ type: Array })
  propertyValueScopeOptions!: PropertyValueScopeOptionApiModel[];

  @property({ type: Boolean }) loadingScopes = true;

  @property({ type: Boolean }) loadingScopeOptions = false;

  @property({ type: Boolean }) addingVariableValue = false;

  @property({ type: Boolean }) loadingProperties = true;

  @property({ type: Boolean }) loadingPropertyValues = false;

  @property({ type: Array }) properties: PropertyApiModel[] | undefined;

  @property({ type: Array }) environments: string[] | undefined;

  @property({ type: Array }) filteredEnvironments: string[] | undefined;

  @property({ type: Array }) propertyValues:
    | PropertyValueDtoExtended[]
    | undefined;

  @property({ type: Number })
  searchId = 0;

  private propertyName = '';

  private newVariableScope = '';

  @property({ type: Boolean }) existingPropertySelected = false;

  @property({ type: Boolean }) newVariableButtonDisabled = true;

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  @property({ type: String }) radioValue = 'existing';

  public userRoles!: string[];

  static get styles() {
    return css`
      .loader {
        border: 16px solid #f3f3f3; /* Light grey */
        border-top: 16px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 120px;
        height: 120px;
        animation: spin 2s linear infinite;
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
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 390px);
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
      vaadin-combo-box {
        padding: 0px;
      }
    `;
  }

  render() {
    return html`
      <vaadin-radio-group
        style="padding-left: 40px; padding-top: 10px"
        theme="horizontal"
        .value="${this.radioValue}"
        @value-changed="${(e: CustomEvent) => {
          this.radioValue = e.detail.value;
        }}"
      >
        <vaadin-radio-button
          checked
          value="existing"
          label="Edit Existing"
        ></vaadin-radio-button>
        <vaadin-radio-button
          value="new"
          label="Create New"
        ></vaadin-radio-button>
      </vaadin-radio-group>
      <vaadin-button
        theme="icon"
        title="Value Lookup"
        style="position: fixed; right: 10px"
        @click="${() => {
          Router.go('/variables/value-lookup');
        }}"
        ><vaadin-icon
          icon="vaadin:search"
          style="color: cornflowerblue"
        ></vaadin-icon
      ></vaadin-button>
      ${this.radioValue === 'existing'
        ? html`
            <vaadin-details
              opened
              summary="Select Variable Name"
              style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; padding-left: 10px"
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
                  <td style="vertical-align: center;">
                    <vaadin-combo-box
                      id="properties"
                      @value-changed="${this._propNameValueChanged}"
                      .items="${this.properties}"
                      label="Existing Variable Name"
                      placeholder="Select Variable Name"
                      clear-button-visible
                      item-label-path="Name"
                      item-value-path="Name"
                      helper-text="${this.propertyValues
                        ? `Property contains ${this.propertyValues?.length} value(s)`
                        : 'Select Variable for info'}"
                      style="min-width: 600px; margin-left: 5px"
                      ?disabled="${this.deletingVariable}"
                    ></vaadin-combo-box>
                  </td>
                  <td style="vertical-align: center;">
                    <vaadin-button
                      style="--lumo-primary-text-color: red;"
                      ?disabled="${!this.isAdmin ||
                      this.deletingVariable ||
                      !this.existingPropertySelected}"
                      @click="${this.deleteVariable}"
                      >Delete Variable</vaadin-button
                    >
                  </td>
                  <td style="vertical-align: center; min-width: 20px">
                    ${this.deletingVariable
                      ? html`<div
                          style="vertical-align: center"
                          class="small-loader"
                        ></div> `
                      : html``}
                  </td>
                  <td style="vertical-align: center;">
                    <vaadin-checkbox
                      id="is-variable-secure"
                      label="Secure"
                      ?disabled="${!((this.isPowerUser || this.isAdmin) && this.existingPropertySelected)}"
                      @click="${this.updatePropertySecure}"
                    ></vaadin-checkbox>
                  </td>
                </tr>
              </table>
            </vaadin-details>
            <vaadin-details
              opened
              summary="Add Variable Value"
              style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; padding-left: 10px"
            >
              <table>
                <tr>
                  <td style="vertical-align: center; min-width: 20px">
                    ${this.loadingScopes
                      ? html`<div
                          style="vertical-align: center"
                          class="small-loader"
                        ></div> `
                      : html``}
                  </td>
                  <td style="vertical-align: center;">
                    <vaadin-combo-box
                      id="envScope"
                      @value-changed="${this._newVariableValueScopeChanged}"
                      .items="${this.filteredEnvironments}"
                      ?disabled="${!this.existingPropertySelected}"
                      label="Scope"
                      placeholder="Select Variable Scope"
                      style="min-width: 400px; margin-left: 5px"
                      helper-text="Select the environment scope or leave blank for default"
                    ></vaadin-combo-box>
                  </td>
                  <td style="vertical-align: center; min-width: 20px">
                    ${this.loadingScopeOptions
                      ? html`<div
                          style="vertical-align: center"
                          class="small-loader"
                        ></div> `
                      : html``}
                  </td>
                  <td style="vertical-align: center;">
                    <vaadin-combo-box
                      allow-custom-value
                      .items="${this.propertyValueScopeOptions}"
                      item-label-path="ValueOption"
                      item-value-path="ValueOption"
                      .renderer="${this.comboboxRenderer}"
                      id="newVariableValue"
                      ?disabled="${!this.existingPropertySelected}"
                      label="Value"
                      style="min-width: 400px"
                      helper-text="Include a resolver eg. $AnotherVariable$ or specify value directly"
                    ></vaadin-combo-box>
                  </td>
                  <td style="vertical-align: center;">
                    <vaadin-button
                      ?disabled="${!this.existingPropertySelected}"
                      @click="${this._addVariableValueClick}"
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
            </vaadin-details>
            ${this.loadingPropertyValues
              ? html`<div
                  style="vertical-align: center"
                  class="small-loader"
                ></div>`
              : html`<vaadin-grid
                  id="grid"
                  .items="${this.propertyValues}"
                  column-reordering-allowed
                  multi-sort
                  theme="compact row-stripes no-row-borders no-border"
                  all-rows-visible
                  ?disabled="${this.deletingVariable ||
                  !this.existingPropertySelected}"
                  .cellClassNameGenerator="${this.cellClassNameGenerator}"
                >
                  <vaadin-grid-column
                    header="Scope"
                    path="PropertyValueFilter"
                    width="200px"
                    flex-grow="0"
                    resizable
                    .headerRenderer="${this.scopeHeaderRenderer}"
                  ></vaadin-grid-column>
                  <vaadin-grid-column
                    header="Value"
                    resizable
                    auto-width
                    .renderer="${this.variableValueControlsRenderer}"
                    .headerRenderer="${this.valueHeaderRenderer}"
                  ></vaadin-grid-column>
                </vaadin-grid>`}
          `
        : html`
            <vaadin-details
              opened
              summary="Add Variable"
              style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; padding-left: 10px"
            >
              <table>
                <tr>
                  <td style="vertical-align: bottom;">
                    <vaadin-text-field
                      id="newVariable"
                      label="New Variable Name"
                      style="min-width: 400px"
                      ?disabled="${!(this.isPowerUser || this.isAdmin)}"
                      @value-changed="${this.newVariableChanged}"
                    ></vaadin-text-field>
                  </td>
                  <td style="vertical-align: bottom;">
                    <vaadin-checkbox
                      id="variable-secure"
                      label="Secure"
                      ?disabled="${!(this.isPowerUser || this.isAdmin)}"
                    ></vaadin-checkbox>
                    <vaadin-checkbox
                      id="variable-array"
                      label="is Array"
                      ?disabled="${!(this.isPowerUser || this.isAdmin)}"
                    ></vaadin-checkbox>
                  </td>
                  <td style="vertical-align: bottom;">
                    <vaadin-button
                      style="margin: 0px"
                      ?disabled="${this.newVariableButtonDisabled}"
                      @click="${this.createVariable}"
                      >Add Variable</vaadin-button
                    >
                  </td>
                  <td style="vertical-align: center;">
                    ${this.creatingVariable
                      ? html`<div
                          style="vertical-align: bottom"
                          class="small-loader"
                        ></div>`
                      : html``}
                  </td>
                </tr>
              </table>
            </vaadin-details>
          `}
    `;
  }

  constructor() {
    super();
    this.getUserRoles();
  }

  private getUserRoles() {
    const gc = GlobalCache.getInstance();
    if (gc.userRoles === undefined) {
      gc.allRolesResp?.subscribe({
        next: (userRoles: string[]) => {
          this.setUserRoles(userRoles);
        }
      });
    } else {
      this.setUserRoles(gc.userRoles);
    }
  }

  private setUserRoles(userRoles: string[]) {
    this.userRoles = userRoles;
    this.isAdmin = this.userRoles.find(p => p === 'Admin') !== undefined;
    this.isPowerUser = this.userRoles.find(p => p === 'PowerUser') !== undefined;
  }

  cellClassNameGenerator(
    _: GridColumn,
    model: GridItemModel<PropertyValueDtoExtended>
  ) {
    const { item } = model;
    let classes = '';

    if (item.IsDuplicate) {
      classes += ' variable-value-error';
    }

    return classes;
  }

  scopeHeaderRenderer(root: HTMLElement) {
    render(
      html`
        <vaadin-grid-sorter path="PropertyValueFilter" direction="asc"
          >Scope</vaadin-grid-sorter
        >
        <vaadin-grid-filter path="PropertyValueFilter">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            style="width: 100px"
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>
      `,
      root
    );

    const filter: GridFilter = root.querySelector(
      'vaadin-grid-filter'
    ) as GridFilter;
    root
      .querySelector('vaadin-text-field')!
      .addEventListener('value-changed', (e: any) => {
        filter.value = e.detail.value;
      });
  }

  valueHeaderRenderer(root: HTMLElement) {
    render(
      html`
        <vaadin-grid-sorter path="Value">Value</vaadin-grid-sorter>
        <vaadin-grid-filter path="Value">
          <vaadin-text-field
            clear-button-visible
            slot="filter"
            focus-target
            theme="small"
          ></vaadin-text-field>
        </vaadin-grid-filter>
      `,
      root
    );

    const filter: GridFilter = root.querySelector(
      'vaadin-grid-filter'
    ) as GridFilter;
    root
      .querySelector('vaadin-text-field')!
      .addEventListener('value-changed', (e: any) => {
        filter.value = e.detail.value;
      });
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

  validateNewVariable() {
    const textField = this.shadowRoot?.querySelector(
      '#newVariable'
    ) as unknown as TextField;
    const found = this.properties?.find(
      value => value.Name === textField.value
    );
    if (found) {
      this.newVariableButtonDisabled = true;
      return;
    }
    if (textField.value === '') {
      this.newVariableButtonDisabled = true;
      return;
    }
    this.newVariableButtonDisabled = false;
  }

  newVariableChanged(data: CustomEvent) {
    if (data) {
      const combo = data.target as TextField;
      this.newVariableName = combo.value.trim();
      this.validateNewVariable();
    }
  }

  deleteVariable() {
    const existingProps = this.shadowRoot?.querySelector(
      '#properties'
    ) as unknown as ComboBox;
    const selected = existingProps.selectedItem as PropertyApiModel;

    const answer = confirm(
      `Are you sure you want to delete the variable ${selected.Name} and all of its ${this.propertyValues?.length} value(s)?`
    );

    if (existingProps && answer) {
      this.deletingVariable = true;
      const api = new PropertiesApi();
      api
        .propertiesDelete({
          requestBody: [selected.Name ?? '']
        })
        .subscribe({
          next: (data: Response[]) => {
            if (data.every(r => r.Status === 'success')) {
              this.getAllVariableNames();
              existingProps.selectedItem = undefined;

              const msg = `Variable with name: ${selected.Name} deleted`;
              Notification.show(msg, {
                theme: 'success',
                position: 'bottom-start'
              });
            } else if (data.some(r => r.Status === 'success')) {
              this.errorAlert(data);
              this.getAllVariableNames();
              existingProps.selectedItem = undefined;

              const msg = `Variable with name: ${selected.Name} removed with errors`;
              const notification = new WarningNotification();
              notification.setAttribute('warningMessage', msg);
              this.shadowRoot?.appendChild(notification);
              notification.open();
            } else {
              this.errorAlert(data);
            }
            this.deletingVariable = false;
          },
          error: (err: any) => {
            this.errorAlert(err);
          },
          complete: () => console.log('done deleting variable')
        });
    }
  }

  createVariable() {
    this.creatingVariable = true;
    const checkboxSecure = this.shadowRoot?.querySelector(
      '#variable-secure'
    ) as unknown as Checkbox;
    const checkboxArray = this.shadowRoot?.querySelector(
      '#variable-array'
    ) as unknown as Checkbox;
    const prop: PropertyApiModel = {
      Name: this.newVariableName,
      Secure: checkboxSecure.checked,
      IsArray: checkboxArray.checked
    };

    const api = new PropertiesApi();
    api.propertiesPost({ propertyApiModel: [prop] }).subscribe({
      next: (data: Response[]) => {
        if (data[0].Status === 'success') {
          this.radioValue = 'existing';

          const newProp = data[0].Item as PropertyApiModel;
          this.properties?.push(newProp);
          this.properties = JSON.parse(
            JSON.stringify(this.properties?.sort(this.sortProperties))
          );

          this.creatingVariable = false;
        } else {
          this.errorAlert(data);
        }
      },
      error: (err: any) => this.errorAlert(err),
      complete: () => console.log('done creating variable')
    });
  }

  protected firstUpdated(
    _changedProperties: PropertyValueMap<any> | Map<PropertyKey, unknown>
  ): void {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'variable-value-deleted',
      this.variableValueDeleted as EventListener
    );
    this.getAllVariableNames();
    this.getEnvironments();
  }

  private getEnvironments() {
    const api2 = new RefDataEnvironmentsApi();
    api2.refDataEnvironmentsGetAllEnvironmentNamesGet().subscribe({
      next: (data: string[]) => {
        this.removeExistingScopesFromSelectable();
        this.environments = data;
        this.loadingScopes = false;
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading environments')
    });
  }

  private getAllVariableNames() {
    const api = new PropertiesApi();
    api.propertiesGet().subscribe({
      next: (data: PropertyApiModel[]) => {
        this.properties = data.sort(this.sortProperties);
        this.loadingProperties = false;
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading properties')
    });
  }

  sortProperties(a: PropertyApiModel, b: PropertyApiModel): number {
    if (String(a.Name) > String(b.Name)) return 1;
    return -1;
  }

  _propNameValueChanged(data: CustomEvent) {
    if (data) {
      const combo = data.target as ComboBox;
      this.propertyName = combo.value;

      if (this.newVariableName !== '') {
        const newfoundProp = this.properties?.find(
          value => value.Name === this.newVariableName
        );
        if (combo) {
          combo.selectedItem = newfoundProp;
          this.newVariableName = '';
        }
      }

      const existingProperty = this.properties?.find(
        value => value.Name === this.propertyName
      );

      const checkboxSecure = this.shadowRoot?.querySelector(
        '#is-variable-secure'
      ) as unknown as Checkbox;

      if (existingProperty) {
        this.existingPropertySelected = true;
        checkboxSecure.checked = existingProperty?.Secure ?? false;
        this.loadVariableValues();
      } else {
        this.existingPropertySelected = false;
        checkboxSecure.checked = false;
        this.propertyValues = [];
      }
    }
  }

  _newVariableValueScopeChanged(data: CustomEvent) {
    if (data) {
      const combo = data.target as ComboBox;
      this.newVariableScope = combo.value;
      if (!this.newVariableScope)
      {
        return;
      }
      this.loadingScopeOptions = true;

      const api = new PropertyValuesApi();
      api
        .propertyValuesScopeOptionsGet({
          propertyValueScope: this.newVariableScope
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
          complete: () =>
            console.log('done loading variable value scope options')
        });
    }
  }

  removeItem<T>(arr: Array<T>, value: T): Array<T> {
    const index = arr.indexOf(value);
    if (index > -1) {
      arr.splice(index, 1);
    }
    return arr;
  }

  private loadVariableValues() {
    if (this.propertyName !== '') {
      this.loadingPropertyValues = true;
      const api = new PropertyValuesApi();
      api.propertyValuesGet({ propertyName: this.propertyName }).subscribe({
        next: (data: PropertyValueDto[]) => {
          this.setVariableValues(data);
        },
        error: (err: any) => {
          if (err.status !== 404) {
            this.errorAlert(err);
          }
          this.setVariableValues([]);
        },
        complete: () => console.log('done loading variable values')
      });
    }
  }

  updatePropertySecure(event: Event) {
    const checkbox = event.target as Checkbox;
    const existingProperty = this.properties?.find(
      value => value.Name === this.propertyName
    );

    if (existingProperty && this.propertyName) {
      // Store the original state for proper reverting
      const originallySecured = existingProperty.Secure ?? false;
      
      let confirmMessage = '';
      
      if (!originallySecured) {
        confirmMessage = `Are you sure you want to mark property "${this.propertyName}" as secure?\n\nThis will automatically encrypt all existing property values for this property. This action cannot be undone.`;
      } else if (originallySecured) {
        confirmMessage = `Are you sure you want to mark property "${this.propertyName}" as non-secure?\n\nThis will not decrypt existing values, but new values will be stored in plaintext.`;
      }
      
      const revertCheckboxState = () => {
        event.preventDefault();
        checkbox.checked = originallySecured;
      };

      if (!confirm(confirmMessage)) {
        revertCheckboxState();
        return;
      }

      const updatedProperty: PropertyApiModel = {
        ...existingProperty,
        Secure: !originallySecured
      };

      const api = new PropertiesApi();
      const requestBody = { [this.propertyName]: updatedProperty };
      
      api.propertiesPut({ requestBody }).subscribe({
        next: (data: Response[]) => {
          if (data[0].Status === 'success') {
            // Update the local property object
            existingProperty.Secure = !originallySecured;
            
            let message = '';
            if (!originallySecured) {
              message = `Property "${this.propertyName}" secured successfully. Existing property values have been automatically encrypted.`;
            } else {
              message = `Property "${this.propertyName}" unsecured successfully.`;
            }
            
            Notification.show(message, { 
              position: 'bottom-start', 
              theme: 'success'
            });
            
            // Reload page data to reflect changes
            this.loadVariableValues();
          } else {
            revertCheckboxState();
            this.errorAlert([data[0]]);
          }
        },
        error: (err: any) => {
          revertCheckboxState();
          this.errorAlert(err);
        },
        complete: () => console.log('done updating property security')
      });
    }
  }

  private setVariableValues(data: PropertyValueDto[]) {
    this.propertyValues = data;
    this.propertyValues.forEach(pv => {
      pv.IsDuplicate =
        (this.propertyValues?.filter(
          x => pv.PropertyValueFilter === x.PropertyValueFilter
        ).length ?? 0) > 1;
    });
    this.getEnvironments();
    this.loadingPropertyValues = false;
  }

  private removeExistingScopesFromSelectable() {
    let envs: string[] = this.environments as string[];
    this.propertyValues?.forEach(pv => {
      envs = this.removeItem(envs, pv.PropertyValueFilter ?? '');
    });
    this.filteredEnvironments = envs;
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
        Value: textField.value.trim(),
        PropertyValueFilter: this.newVariableScope
      };
      if (propertyValueDto.PropertyValueFilter === '') {
        propertyValueDto.PropertyValueFilter = undefined;
        if (
          this.propertyValues?.find(
            pv =>
              pv.PropertyValueFilter === '' ||
              pv.PropertyValueFilter === undefined ||
              pv.PropertyValueFilter === null
          )
        ) {
          const notification = new ErrorNotification();
          notification.setAttribute(
            'errorMessage',
            'This variable already contains a default scoped value!'
          );
          this.shadowRoot?.appendChild(notification);
          notification.open();
          this.addingVariableValue = false;
          return;
        }
      }
      api
        .propertyValuesPost({
          propertyValueDto: [propertyValueDto]
        })
        .subscribe({
          next: (value: Response[]) => {
            if (value[0].Status === 'success') {
              value.forEach((repValue: Response) => {
                console.log(repValue.Status);
              });
              const pv = value[0].Item as PropertyValueDto;

              const findIndex = this.filteredEnvironments?.findIndex(
                env => env === pv.PropertyValueFilter
              );
              if (findIndex !== undefined) {
                this.filteredEnvironments?.splice(findIndex, 1);
                this.filteredEnvironments?.sort();
                this.filteredEnvironments = JSON.parse(
                  JSON.stringify(this.filteredEnvironments)
                );
                const combobox = this.shadowRoot?.querySelector(
                  '#envScope'
                ) as unknown as ComboBox;
                if (combobox) combobox.value = '';
              }

              this.loadVariableValues();
              this.addingVariableValue = false;
            } else {
              this.errorAlert(value);
              this.addingVariableValue = false;
            }
          },
          error: (err: any) => {
            this.errorAlert(err);
          },
          complete: () => console.log('done adding variable value')
        });
    }
  }

  variableValueDeleted(e: CustomEvent) {
    if (e.detail.data) {
      const data = e.detail.data as Response[];
      if (data[0].Status === 'success') {
        const pv = data[0].Item as PropertyValueDto;
        if (pv.PropertyValueFilter !== undefined) {
          this.filteredEnvironments?.push(pv.PropertyValueFilter ?? '');
          this.filteredEnvironments?.sort();
          this.filteredEnvironments = JSON.parse(
            JSON.stringify(this.filteredEnvironments)
          );
        }
        this.loadVariableValues();
      } else {
        this.errorAlert(data);
      }
    }
  }

  variableValueControlsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<PropertyValueDtoExtended>
  ) {
    let dup = '';
    if (model.item.IsDuplicate) {
      dup = 'WARNING: duplicate value located!';
    }

    render(
      html`<variable-value-controls
        .value="${model.item}"
        .additionalInformation="${dup}"
        style="min-width:150px"
      >
      </variable-value-controls>`,
      root
    );
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
        if (scope.Property != undefined) {
          msg = `${scope.Property?.Name} - Scope '${scopeStr}' - ${element.Status}`;
        } else {
          msg = `${element.Status}`;
        }
      } else {
        msg = `${element.Item} - ${element.Status}`;
      }
    }
    return msg;
  }
}
