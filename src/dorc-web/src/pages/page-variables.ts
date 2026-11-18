import { comboBoxRenderer } from '@vaadin/combo-box/lit';
import { columnBodyRenderer, columnHeaderRenderer } from '@vaadin/grid/lit';
import { confirmPrompt } from '../components/confirm-prompt';
import '@vaadin/button';
import { Checkbox } from '@vaadin/checkbox';
import '@vaadin/combo-box';
import '@vaadin/checkbox';
import { ComboBox } from '@vaadin/combo-box';
import '@vaadin/details';
import '@vaadin/grid';
import { GridCellPartNameGenerator } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid-sorter';
import '@vaadin/horizontal-layout';
import '@vaadin/item';
import '@vaadin/list-box';
import { Notification } from '@vaadin/notification';
import '@vaadin/radio-group';
import '@vaadin/text-area';
import '@vaadin/text-field';
import { TextField } from '@vaadin/text-field';
import { css, PropertyValueMap } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/grid-button-groups/variable-value-controls';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { ErrorNotification } from '../components/notifications/error-notification';
import { WarningNotification } from '../components/notifications/warning-notification';
import {
  EnvironmentApiModel,
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
import { navigate } from '../router/router';
import '@vaadin/tooltip';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

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
    PropertyValueDtoExtended[] | undefined;

  private allPropertyValues: PropertyValueDtoExtended[] | undefined;

  @state() private _editingValueId: number | undefined;

  private scopeFilterValue = '';

  private valueFilterValue = '';

  @property({ type: Number })
  searchId = 0;

  private propertyName = '';

  private newVariableScope = '';

  private scopeOptionsGeneration = 0;

  private variableValuesGeneration = 0;

  private addValueGeneration = 0;

  @state() selectedVariableScopes: string[] = [];

  @property({ type: Boolean }) existingPropertySelected = false;

  @property({ type: Boolean }) newVariableButtonDisabled = true;

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  @property({ type: Boolean }) hasEditableEnvironment = false;

  @property({ type: String }) radioValue = 'existing';

  public userRoles!: string[];

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;
      }
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
        flex: 1;
        min-height: 200px;
      }
      vaadin-grid#grid::part(variable-value-error) {
        background-color: var(--dorc-highlight-bg);
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

      .variable-selector-table {
        width: fit-content;
        max-width: 100%;
      }

      .variable-selector-cell {
        width: auto;
      }

      .variable-selector-combo {
        width: clamp(30rem, 46vw, 52rem);
        min-width: 30rem;
        max-width: none;
        margin-left: var(--lumo-space-xs);
      }

      .variable-selector-actions {
        white-space: nowrap;
      }

      @media (max-width: 768px) {
        .variable-selector-table {
          width: 100%;
        }

        .variable-selector-cell {
          width: 100%;
        }

        .variable-selector-combo {
          width: 100%;
          min-width: 0;
          margin-left: 0;
        }
      }

      .scope-chips-row {
        display: flex;
        flex-wrap: wrap;
        gap: 6px;
        align-items: center;
        margin-top: 4px;
        margin-left: 5px;
      }

      .scope-chip {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 2px 8px;
        border-radius: 999px;
        border: 1px solid var(--dorc-border-color, rgba(128, 128, 128, 0.3));
        background: var(--dorc-chip-bg, rgba(59, 130, 246, 0.14));
        color: var(--dorc-chip-text, inherit);
        font-size: var(--lumo-font-size-xs, 0.78rem);
      }

      .scope-chip-remove {
        border: none;
        background: transparent;
        color: inherit;
        cursor: pointer;
        padding: 0 2px;
        font-size: 0.85rem;
        line-height: 1;
      }

      .value-resolver-stack {
        display: flex;
        flex-wrap: wrap;
        gap: 8px;
        align-items: flex-end;
        width: 100%;
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
        aria-label="Value Lookup"
        style="position: fixed; right: 10px"
        @click="${() => {
          void navigate('/variables/value-lookup');
        }}"
      >
        <vaadin-tooltip slot="tooltip" text="Value Lookup"></vaadin-tooltip
        ><vaadin-icon
          icon="vaadin:search"
          style="color: var(--dorc-link-color)"
        ></vaadin-icon
      ></vaadin-button>
      ${
        this.radioValue === 'existing'
          ? html`
              <vaadin-details
                opened
                summary="Select Variable Name"
                style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; padding-left: 10px"
              >
                <table class="variable-selector-table">
                  <tr>
                    <td style="vertical-align: center; min-width: 20px">
                      ${
                        this.loadingProperties
                          ? html`<div
                              style="vertical-align: center"
                              class="small-loader"
                            ></div> `
                          : html``
                      }
                    </td>
                    <td
                      class="variable-selector-cell"
                      style="vertical-align: center;"
                    >
                      <vaadin-combo-box
                        class="variable-selector-combo"
                        id="properties"
                        @value-changed="${this._propNameValueChanged}"
                        .items="${this.properties}"
                        ${comboBoxRenderer(this.propertyNameRenderer, [])}
                        label="Existing Variable Name"
                        placeholder="Select Variable Name"
                        clear-button-visible
                        item-label-path="Name"
                        item-value-path="Name"
                        helper-text="${
                          this.propertyValues
                            ? `Property contains ${this.propertyValues?.length} value(s)`
                            : 'Select Variable for info'
                        }"
                        ?disabled="${this.deletingVariable}"
                      ></vaadin-combo-box>
                    </td>
                    <td
                      class="variable-selector-actions"
                      style="vertical-align: center;"
                    >
                      <vaadin-button
                        style="--lumo-primary-text-color: var(--dorc-error-color);"
                        ?disabled="${
                          !this.canDeleteSelectedVariable() ||
                          this.deletingVariable ||
                          !this.existingPropertySelected
                        }"
                        @click="${this.deleteVariable}"
                        >Delete Variable</vaadin-button
                      >
                    </td>
                    <td style="vertical-align: center; min-width: 20px">
                      ${
                        this.deletingVariable
                          ? html`<div
                              style="vertical-align: center"
                              class="small-loader"
                            ></div> `
                          : html``
                      }
                    </td>
                    <td
                      class="variable-selector-actions"
                      style="vertical-align: center;"
                    >
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
                style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; padding-left: 10px"
              >
                <table style="width: 100%">
                  <tr>
                    <td style="vertical-align: center; min-width: 20px">
                      ${
                        this.loadingScopes
                          ? html`<div
                              style="vertical-align: center"
                              class="small-loader"
                            ></div> `
                          : html``
                      }
                    </td>
                    <td style="vertical-align: top;">
                      <vaadin-combo-box
                        id="envScope"
                        @value-changed="${this._newVariableValueScopeChanged}"
                        .items="${this.filteredEnvironments}"
                        ?disabled="${!this.existingPropertySelected}"
                        clear-button-visible
                        label="Scope"
                        placeholder="Select Variable Scope (or add multiple)"
                        style="min-width: 340px; margin-left: 5px"
                        helper-text="Select environment scope(s) or include (default)"
                      ></vaadin-combo-box>
                      <div class="scope-chips-row" role="group" aria-label="Selected target scopes">
                        <vaadin-button
                          id="addDefaultScopeBtn"
                          theme="small tertiary"
                          ?disabled="${!this.existingPropertySelected || this.hasDefaultScopedValue()}"
                          @click="${this.toggleDefaultScopeSelection}"
                        >
                          ${
                            this.selectedVariableScopes.includes('')
                              ? '✓ (default) queued'
                              : '+ Queue (default)'
                          }
                        </vaadin-button>
                        <vaadin-button
                          id="queueCurrentScopeBtn"
                          theme="small tertiary"
                          ?disabled="${!this.existingPropertySelected || !this.newVariableScope}"
                          @click="${this.queueCurrentEnvironmentScope}"
                        >
                          + Queue selected scope
                        </vaadin-button>
                        ${this.selectedVariableScopes.map(
                          scope => html`
                            <span class="scope-chip">
                              <span>${scope === '' ? '(default)' : scope}</span>
                              <button
                                type="button"
                                class="scope-chip-remove"
                                aria-label="Remove scope ${scope === '' ? '(default)' : scope}"
                                @click="${() => this.removeQueuedScope(scope)}"
                              >
                                ×
                              </button>
                            </span>
                          `
                        )}
                      </div>
                    </td>
                    <td style="vertical-align: center; min-width: 20px">
                      ${
                        this.loadingScopeOptions
                          ? html`<div
                              style="vertical-align: center"
                              class="small-loader"
                            ></div> `
                          : html``
                      }
                    </td>
                    <td style="vertical-align: top; width: 100%;">
                      <div class="value-resolver-stack">
                        <vaadin-text-field
                          id="newVariableValue"
                          ?disabled="${!this.existingPropertySelected}"
                          clear-button-visible
                          label="Value"
                          placeholder="e.g. \\\\$ServerNames_STP_Strategies$\\E$\\Apps\\..."
                          style="min-width: 320px; flex: 1 1 320px;"
                          helper-text="Specify value directly or insert resolver tokens without overwriting existing text"
                        ></vaadin-text-field>
                        <vaadin-combo-box
                          id="scopeOptionPicker"
                          .items="${this.propertyValueScopeOptions}"
                          item-label-path="ValueOption"
                          item-value-path="ValueOption"
                          ${comboBoxRenderer(this.comboboxRenderer, [])}
                          ?disabled="${!this.existingPropertySelected}"
                          clear-button-visible
                          label="Insert Resolver Token"
                          placeholder="Insert $ServerNames_&lt;Tag&gt;$..."
                          style="min-width: 260px; flex: 0 1 300px;"
                          helper-text="Inserts into Value at cursor"
                          @value-changed="${this._onScopeOptionPicked}"
                        ></vaadin-combo-box>
                      </div>
                    </td>
                    <td style="vertical-align: center;">
                      <vaadin-button
                        ?disabled="${!this.existingPropertySelected ||
                        this.addingVariableValue}"
                        @click="${this._addVariableValueClick}"
                        >Add Variable Value</vaadin-button
                      >
                    </td>
                    <td style="vertical-align: center; min-width: 20px">
                      ${
                        this.addingVariableValue
                          ? html`<div
                              style="vertical-align: center"
                              class="small-loader"
                            ></div> `
                          : html``
                      }
                    </td>
                  </tr>
                </table>
              </vaadin-details>
              ${
                this.loadingPropertyValues
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
                      ?disabled="${
                        this.deletingVariable || !this.existingPropertySelected
                      }"
                      .cellPartNameGenerator="${this.cellPartNameGenerator}"
                    >
                      <vaadin-grid-column
                        header="Scope"
                        path="PropertyValueFilter"
                        width="200px"
                        flex-grow="0"
                        resizable
                        ${columnHeaderRenderer(this.scopeHeaderRenderer, [])}
                        ${columnBodyRenderer(this.scopeValueRenderer, [])}
                      ></vaadin-grid-column>
                      <vaadin-grid-column
                        header="Value"
                        resizable
                        flex-grow="1"
                        width="20rem"
                        ${columnBodyRenderer(
                          this.variableValueControlsRenderer,
                          [this._editingValueId]
                        )}
                        ${columnHeaderRenderer(this.valueHeaderRenderer, [])}
                      ></vaadin-grid-column>
                    </vaadin-grid>`
              }
            `
          : html`
              <vaadin-details
                opened
                summary="Add Variable"
                style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; padding-left: 10px"
              >
                <table>
                  <tr>
                    <td style="vertical-align: bottom;">
                      <vaadin-text-field
                        id="newVariable"
                        label="New Variable Name"
                        style="min-width: 400px"
                        ?disabled="${!(this.isPowerUser || this.isAdmin || this.hasEditableEnvironment)}"
                        @value-changed="${this.newVariableChanged}"
                      ></vaadin-text-field>
                    </td>
                    <td style="vertical-align: bottom;">
                      <vaadin-checkbox
                        id="variable-secure"
                        label="Secure"
                        ?disabled="${!(this.isPowerUser || this.isAdmin || this.hasEditableEnvironment)}"
                      ></vaadin-checkbox>
                      <vaadin-checkbox
                        id="variable-array"
                        label="is Array"
                        ?disabled="${!(this.isPowerUser || this.isAdmin || this.hasEditableEnvironment)}"
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
                      ${
                        this.creatingVariable
                          ? html`<div
                              style="vertical-align: bottom"
                              class="small-loader"
                            ></div>`
                          : html``
                      }
                    </td>
                  </tr>
                </table>
              </vaadin-details>
            `
      }
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
    this.isPowerUser =
      this.userRoles.find(p => p === 'PowerUser') !== undefined;
  }

  cellPartNameGenerator: GridCellPartNameGenerator<PropertyValueDtoExtended> = (
    _column,
    model
  ) => {
    const { item } = model;
    let parts = '';

    if (item.IsDuplicate) {
      parts += ' variable-value-error';
    }

    return parts;
  };

  scopeValueRenderer(item: PropertyValueDtoExtended) {
    const scope = item.PropertyValueFilter;
    const isDefault = !scope;
    return html`<span
      style="${isDefault ? 'font-style: italic; color: var(--lumo-secondary-text-color);' : ''}"
    >
      ${isDefault ? '(default)' : scope}
    </span>`;
  }

  scopeHeaderRenderer = () => {
    return html`
      <vaadin-horizontal-layout style="align-items: center;" theme="spacing-xs">
        <vaadin-grid-sorter path="PropertyValueFilter"></vaadin-grid-sorter>
        <vaadin-text-field
          placeholder="Scope"
          clear-button-visible
          focus-target
          style="width: 100px"
          theme="small"
          @input="${(e: InputEvent) => {
            const textField = e.target as HTMLInputElement;
            this.scopeFilterValue = textField?.value ?? '';
            this.applyFilters();
          }}"
        ></vaadin-text-field>
      </vaadin-horizontal-layout>
    `;
  };

  valueHeaderRenderer = () => {
    return html`
      <vaadin-horizontal-layout style="align-items: center;" theme="spacing-xs">
        <vaadin-grid-sorter path="Value"></vaadin-grid-sorter>
        <vaadin-text-field
          placeholder="Value"
          clear-button-visible
          focus-target
          style="width: 100px"
          theme="small"
          @input="${(e: InputEvent) => {
            const textField = e.target as HTMLInputElement;
            this.valueFilterValue = textField?.value ?? '';
            this.applyFilters();
          }}"
        ></vaadin-text-field>
      </vaadin-horizontal-layout>
    `;
  };

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

  private propertyNameRenderer = (property: PropertyApiModel) =>
    html`<div title="${property.Name}">${property.Name}</div>`;

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

  private canDeleteSelectedVariable(): boolean {
    if (this.isAdmin) return true;
    return (
      this.isPowerUser &&
      !this.loadingPropertyValues &&
      this.allPropertyValues !== undefined &&
      this.allPropertyValues.length === 0
    );
  }

  async deleteVariable() {
    const existingProps = this.shadowRoot?.querySelector(
      '#properties'
    ) as unknown as ComboBox;
    const selected = existingProps.selectedItem as PropertyApiModel;

    const answer = await confirmPrompt(
      `Are you sure you want to delete the variable ${selected.Name} and all of its ${this.propertyValues?.length} value(s)?`
    );

    if (existingProps && answer) {
      this.deletingVariable = true;
      const api = new PropertiesApi(dorcApiConfiguration);
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
            this.deletingVariable = false;
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

    const api = new PropertiesApi(dorcApiConfiguration);
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
          this.creatingVariable = false;
          this.errorAlert(data);
        }
      },
      error: (err: any) => {
        this.creatingVariable = false;
        this.errorAlert(err);
      },
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
    this.addEventListener('editing-started', ((e: CustomEvent) => {
      this._editingValueId = e.detail.id;
    }) as EventListener);
    this.addEventListener('editing-cancelled', (() => {
      this._editingValueId = undefined;
    }) as EventListener);
    this.getAllVariableNames();
    this.getEnvironments();
    this.loadScopeOptions('');
  }

  private getEnvironments() {
    const api2 = new RefDataEnvironmentsApi(dorcApiConfiguration);
    api2.refDataEnvironmentsGet({}).subscribe({
      next: (data: EnvironmentApiModel[]) => {
        const editableNames = data
          .filter(
            env => Boolean(env.UserEditable) && Boolean(env.EnvironmentName)
          )
          .map(env => env.EnvironmentName as string)
          .sort();
        this.hasEditableEnvironment = editableNames.length > 0;
        this.environments = editableNames;
        this.removeExistingScopesFromSelectable();
        this.loadingScopes = false;
      },
      error: (err: any) => {
        this.loadingScopes = false;
        console.error(err);
      },
      complete: () => console.log('done loading environments')
    });
  }

  private getAllVariableNames() {
    const api = new PropertiesApi(dorcApiConfiguration);
    api.propertiesGet().subscribe({
      next: (data: PropertyApiModel[]) => {
        this.properties = data.sort(this.sortProperties);
        this.loadingProperties = false;
      },
      error: (err: any) => {
        this.loadingProperties = false;
        console.error(err);
      },
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
      combo.title = this.propertyName;

      // Column filter text fields are recreated for the new grid instance and
      // appear empty, but the stale filter values would otherwise still be
      // applied silently to the newly selected variable's values.
      this.scopeFilterValue = '';
      this.valueFilterValue = '';

      if (this.newVariableName !== '') {
        const newfoundProp = this.properties?.find(
          value => value.Name === this.newVariableName
        );
        if (combo) {
          combo.selectedItem = newfoundProp;
          combo.title = combo.value;
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
        this.selectedVariableScopes = [];
        this.addingVariableValue = false;
        this.addValueGeneration++;
        checkboxSecure.checked = existingProperty?.Secure ?? false;
        this.loadVariableValues();
        this.loadScopeOptions(this.newVariableScope);
      } else {
        this.variableValuesGeneration++;
        this.loadingPropertyValues = false;
        this.addingVariableValue = false;
        this.addValueGeneration++;
        this.existingPropertySelected = false;
        this.selectedVariableScopes = [];
        checkboxSecure.checked = false;
        this.propertyValues = [];
        this.allPropertyValues = [];
      }
    }
  }

  _newVariableValueScopeChanged(data: CustomEvent) {
    if (data) {
      const combo = data.target as ComboBox;
      this.newVariableScope = combo.value ?? '';
      this.loadScopeOptions(this.newVariableScope);
    }
  }

  public loadScopeOptions(scope: string) {
    const generation = ++this.scopeOptionsGeneration;
    this.loadingScopeOptions = true;
    this.propertyValueScopeOptions = [];

    const api = new PropertyValuesApi(dorcApiConfiguration);
    api
      .propertyValuesScopeOptionsGet({
        propertyValueScope: scope || undefined
      })
      .subscribe({
        next: (value: PropertyValueScopeOptionApiModel[]) => {
          if (generation !== this.scopeOptionsGeneration) return;
          this.propertyValueScopeOptions = value.sort((a, b) => {
            if (String(a.ValueOption) > String(b.ValueOption)) return 1;
            return -1;
          });
          this.loadingScopeOptions = false;
        },
        error: (err: any) => {
          if (generation !== this.scopeOptionsGeneration) return;
          this.propertyValueScopeOptions = [];
          this.loadingScopeOptions = false;
          this.errorAlert(err);
        },
        complete: () =>
          console.log('done loading variable value scope options')
      });
  }

  public insertTokenIntoValue(token: string) {
    if (!token) return;
    const textField = this.shadowRoot?.querySelector(
      '#newVariableValue'
    ) as unknown as (TextField & { inputElement?: HTMLInputElement }) | null;
    if (!textField) return;
    const current = textField.value ?? '';
    const innerInput =
      textField.inputElement ??
      (textField.querySelector('input') as HTMLInputElement | null) ??
      (textField.shadowRoot?.querySelector('input') as HTMLInputElement | null);
    if (
      innerInput &&
      typeof innerInput.selectionStart === 'number' &&
      typeof innerInput.selectionEnd === 'number'
    ) {
      const start = innerInput.selectionStart;
      const end = innerInput.selectionEnd;
      const nextVal = current.slice(0, start) + token + current.slice(end);
      textField.value = nextVal;
      innerInput.value = nextVal;
      const nextPos = start + token.length;
      innerInput.setSelectionRange(nextPos, nextPos);
    } else {
      textField.value = current + token;
    }
  }

  _onScopeOptionPicked(event: CustomEvent<{ value?: string }>) {
    const picker = event.target as ComboBox | null;
    const token = event.detail?.value ?? picker?.value ?? '';
    if (!token) return;
    this.insertTokenIntoValue(token);
    if (picker) {
      picker.value = '';
    }
  }

  private hasDefaultScopedValue(): boolean {
    const sourceValues = this.allPropertyValues?.length
      ? this.allPropertyValues
      : this.propertyValues;
    return Boolean(
      sourceValues?.some(
        pv =>
          pv.PropertyValueFilter === '' ||
          pv.PropertyValueFilter === undefined ||
          pv.PropertyValueFilter === null
      )
    );
  }

  public toggleDefaultScopeSelection() {
    if (this.selectedVariableScopes.includes('')) {
      this.selectedVariableScopes = this.selectedVariableScopes.filter(
        s => s !== ''
      );
    } else {
      this.selectedVariableScopes = ['', ...this.selectedVariableScopes];
    }
  }

  public queueCurrentEnvironmentScope() {
    const scope = this.newVariableScope.trim();
    if (!scope) return;
    if (!this.selectedVariableScopes.includes(scope)) {
      this.selectedVariableScopes = [...this.selectedVariableScopes, scope];
    }
    const combobox = this.shadowRoot?.querySelector(
      '#envScope'
    ) as unknown as ComboBox | null;
    this.newVariableScope = '';
    if (combobox) {
      combobox.value = '';
    }
  }

  public removeQueuedScope(scope: string) {
    this.selectedVariableScopes = this.selectedVariableScopes.filter(
      s => s !== scope
    );
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
      const requestedProperty = this.propertyName;
      const generation = ++this.variableValuesGeneration;
      this.loadingPropertyValues = true;
      const api = new PropertyValuesApi(dorcApiConfiguration);
      api.propertyValuesGet({ propertyName: requestedProperty }).subscribe({
        next: (data: PropertyValueDto[]) => {
          if (
            generation !== this.variableValuesGeneration ||
            this.propertyName !== requestedProperty
          ) {
            return;
          }
          this.setVariableValues(data);
        },
        error: (err: any) => {
          if (
            generation !== this.variableValuesGeneration ||
            this.propertyName !== requestedProperty
          ) {
            return;
          }
          if (err.status !== 404) {
            this.errorAlert(err);
          }
          this.setVariableValues([]);
        },
        complete: () => console.log('done loading variable values')
      });
    }
  }

  async updatePropertySecure(event: Event) {
    // currentTarget, not target: the click originates on the light-DOM <input>
    // inside vaadin-checkbox, so `event.target` is that input and writing
    // `.checked` to it leaves the host — and the visible tick — unchanged.
    const checkbox = event.currentTarget as Checkbox;
    // Snapshot the property name: it is reassigned by the selector combo, and
    // the PUT below runs after the confirmation. Reading it again would key the
    // request by whatever is selected then, not by the property the user was
    // asked about.
    const propertyName = this.propertyName;
    const existingProperty = this.properties?.find(
      value => value.Name === propertyName
    );

    if (existingProperty && propertyName) {
      // Store the original state for proper reverting
      const originallySecured = existingProperty.Secure ?? false;

      let confirmMessage = '';

      if (!originallySecured) {
        confirmMessage = `Are you sure you want to mark property "${propertyName}" as secure?\n\nThis will automatically encrypt all existing property values for this property. This action cannot be undone.`;
      } else if (originallySecured) {
        confirmMessage = `Are you sure you want to mark property "${propertyName}" as non-secure?\n\nThis will not decrypt existing values, but new values will be stored in plaintext.`;
      }

      // preventDefault() is dropped: the dialog is awaited, so by the time this
      // runs the click has long since been dispatched and it does nothing.
      const revertCheckboxState = () => {
        checkbox.checked = originallySecured;
      };

      if (!(await confirmPrompt(confirmMessage))) {
        revertCheckboxState();
        return;
      }

      const updatedProperty: PropertyApiModel = {
        ...existingProperty,
        Secure: !originallySecured
      };

      const api = new PropertiesApi(dorcApiConfiguration);
      const requestBody = { [propertyName]: updatedProperty };

      api.propertiesPut({ requestBody }).subscribe({
        next: (data: Response[]) => {
          if (data[0].Status === 'success') {
            // Update the local property object
            existingProperty.Secure = !originallySecured;

            let message: string;
            if (!originallySecured) {
              message = `Property "${propertyName}" secured successfully. Existing property values have been automatically encrypted.`;
            } else {
              message = `Property "${propertyName}" unsecured successfully.`;
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
    this.allPropertyValues = data.sort((a, b) => {
      const aScope = a.PropertyValueFilter ?? '';
      const bScope = b.PropertyValueFilter ?? '';
      // Default (empty scope) values should appear first
      if (aScope === '' && bScope !== '') return -1;
      if (aScope !== '' && bScope === '') return 1;
      return aScope.localeCompare(bScope);
    });
    this.allPropertyValues.forEach(pv => {
      pv.IsDuplicate =
        (this.allPropertyValues?.filter(
          x => pv.PropertyValueFilter === x.PropertyValueFilter
        ).length ?? 0) > 1;
    });
    this.applyFilters();
    this.getEnvironments();
    this.loadingPropertyValues = false;
  }

  private applyFilters() {
    if (!this.allPropertyValues) {
      this.propertyValues = undefined;
      return;
    }

    this.propertyValues = this.allPropertyValues.filter(pv => {
      const scopeMatch =
        !this.scopeFilterValue ||
        (pv.PropertyValueFilter?.toLowerCase().includes(
          this.scopeFilterValue.toLowerCase()
        ) ??
          false);
      const valueMatch =
        !this.valueFilterValue ||
        (pv.Value?.toLowerCase().includes(
          this.valueFilterValue.toLowerCase()
        ) ??
          false);
      return scopeMatch && valueMatch;
    });
  }

  private removeExistingScopesFromSelectable() {
    let envs: string[] = this.environments as string[];
    this.propertyValues?.forEach(pv => {
      envs = this.removeItem(envs, pv.PropertyValueFilter ?? '');
    });
    this.filteredEnvironments = envs;
  }

  _addVariableValueClick() {
    if (this.addingVariableValue) return;
    const textField = this.shadowRoot?.querySelector(
      '#newVariableValue'
    ) as unknown as TextField;
    const api = new PropertyValuesApi(dorcApiConfiguration);
    const existingProperty = this.properties?.find(
      value => value.Name === this.propertyName
    );
    if (existingProperty) {
      this.addingVariableValue = true;
      const targetScopes = Array.from(
        new Set(
          this.selectedVariableScopes.length > 0
            ? [
                ...this.selectedVariableScopes,
                ...(this.newVariableScope ? [this.newVariableScope] : [])
              ]
            : [this.newVariableScope]
        )
      );

      if (targetScopes.includes('') && this.hasDefaultScopedValue()) {
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

      const submittedPropertyName = this.propertyName;
      const submittedGeneration = ++this.addValueGeneration;
      const submittedRawValue = textField.value;
      const trimmedValue = submittedRawValue.trim();
      const dtos: PropertyValueDto[] = targetScopes.map(scope => ({
        Property: existingProperty,
        Value: trimmedValue,
        PropertyValueFilter: scope === '' ? undefined : scope
      }));

      api
        .propertyValuesPost({
          propertyValueDto: dtos
        })
        .subscribe({
          next: (value: Response[]) => {
            const succeededScopes = new Set<string>();
            const failedResponses: Response[] = [];

            value.forEach((repValue: Response, idx: number) => {
              const scopeKey = targetScopes[idx] ?? '';
              if (repValue.Status === 'success') {
                console.log(repValue.Status);
                succeededScopes.add(scopeKey);
                if (
                  this.propertyName === submittedPropertyName &&
                  this.addValueGeneration === submittedGeneration
                ) {
                  const pv = repValue.Item as PropertyValueDto | undefined;
                  const envToRemove = pv?.PropertyValueFilter ?? scopeKey;
                  const findIndex = this.filteredEnvironments?.findIndex(
                    env => env === envToRemove
                  );
                  if (findIndex !== undefined && findIndex >= 0) {
                    this.filteredEnvironments?.splice(findIndex, 1);
                  }
                }
              } else {
                failedResponses.push(repValue);
              }
            });

            if (
              this.propertyName === submittedPropertyName &&
              this.addValueGeneration === submittedGeneration
            ) {
              if (succeededScopes.size > 0) {
                if (this.filteredEnvironments) {
                  this.filteredEnvironments.sort();
                  this.filteredEnvironments = JSON.parse(
                    JSON.stringify(this.filteredEnvironments)
                  );
                }
                this.selectedVariableScopes = this.selectedVariableScopes.filter(
                  s => !succeededScopes.has(s)
                );
                if (succeededScopes.has(this.newVariableScope.trim())) {
                  const combobox = this.shadowRoot?.querySelector(
                    '#envScope'
                  ) as unknown as ComboBox;
                  if (combobox) combobox.value = '';
                  this.newVariableScope = '';
                }
                this.loadVariableValues();
              }

              if (failedResponses.length === 0) {
                if (textField && textField.value === submittedRawValue) {
                  textField.value = '';
                }
              }
              if (failedResponses.length > 0) {
                this.errorAlert(failedResponses);
              }
              this.addingVariableValue = false;
            }
          },
          error: (err: any) => {
            if (
              this.propertyName === submittedPropertyName &&
              this.addValueGeneration === submittedGeneration
            ) {
              this.addingVariableValue = false;
              this.errorAlert(err);
            }
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

  variableValueControlsRenderer = (item: PropertyValueDtoExtended) => {
    let dup = '';
    if (item.IsDuplicate) {
      dup = 'WARNING: duplicate value located!';
    }

    return html`<variable-value-controls
      .value="${item}"
      .editing="${item.Id === this._editingValueId}"
      .additionalInformation="${dup}"
    >
    </variable-value-controls>`;
  };

  errorAlert(errs: Response[] | any) {
    console.error(errs);

    if (Array.isArray(errs)) {
      errs.forEach(element => {
        const msg = this.processError(element);
        if (msg !== '') {
          const notification = new ErrorNotification();
          notification.setAttribute('errorMessage', msg);
          this.shadowRoot?.appendChild(notification);
          notification.open();
        }
      });
      return;
    }

    const msg = retrieveErrorMessage(errs, 'An unexpected error occurred.');
    if (msg !== '') {
      const notification = new ErrorNotification();
      notification.setAttribute('errorMessage', msg);
      this.shadowRoot?.appendChild(notification);
      notification.open();
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
