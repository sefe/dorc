import '@vaadin/checkbox';
import { columnBodyRenderer, columnHeaderRenderer } from '@vaadin/grid/lit';
import { css, PropertyValues } from 'lit';
import '../dorc-spinner';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import {
  GridDataProviderCallback,
  GridDataProviderParams,
  GridFilterDefinition,
  GridSorterDefinition
} from '@vaadin/grid/vaadin-grid';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-filter';
import { Grid } from '@vaadin/grid';
import '../grid-button-groups/variable-value-controls';
import '../dismissible-item';
import { ComboBox } from '@vaadin/combo-box';
import { TextField } from '@vaadin/text-field';
import {
  PropertiesApi,
  PropertyApiModel,
  PropertyResolutionTraceApiModel,
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
import { ResponsiveMixin } from '../../helpers/responsive-mixin';
import { retrieveErrorMessage } from '../../helpers/errorMessage-retriever';
import { ErrorNotification } from '../notifications/error-notification';
import { Notification } from '@vaadin/notification';
import { confirmPrompt } from '../confirm-prompt';
import '@vaadin/grid/vaadin-grid-sorter';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';
import { firstValueFrom, Observable } from 'rxjs';
import { defaultPropertyResolutionClientCache } from '../../variable-resolution/property-resolution-client-cache';
import { parsePropertyTokens } from '../../variable-resolution/property-token-parser';
import {
  applyDollarTokenCompletion,
  buildInsertableDollarTokens,
  detectActiveDollarCompletion,
  evaluateDollarValuePreview,
  filterInsertableDollarTokens,
  formatSampleResolvedPreview,
  InsertableDollarTokenOption,
  inspectUnescapedDollars,
  segmentizeDollarValueWithTraces,
  synthesizeScopePreviewTraces
} from '../../variable-resolution/resolution-presentation';

const variableValue = 'PropertyValue';
const variableName = 'Property';
const variableScope = 'PropertyValueScope';
const variableIsShowDefaultProps = 'ShowDefaults';

let _environment: EnvironmentApiModel | undefined;

type ImportedPropertyCsvRow = {
  propertyName: string;
  value: string;
  isSecure: boolean;
};

@customElement('env-variables')
export class EnvVariables extends ResponsiveMixin(PageEnvBase) {
  private secureMessage =
    'This environment is not secure which includes default variables during deployments';

  private secureEnvironmentMessage =
    'This environment is secure (Secure = true): global (default) variable values are excluded during deployments. Required variables must be defined on this environment or inherited from an ancestor environment.';

  @property({ type: Boolean }) loadingProperties = true;

  @property({ type: Boolean }) loadingScopeOptions = true;

  @property({ type: Boolean }) addingVariableValue = false;

  @property({ type: Boolean }) importingProperties = false;

  @property({ type: Boolean }) exportingNonSecureProperties = false;

  @property({ type: Array }) properties: PropertyApiModel[] | undefined;

  @property({ type: Array })
  propertyValueScopeOptions!: PropertyValueScopeOptionApiModel[];

  @query('#grid') grid: Grid | undefined;

  @property({ type: Boolean }) loading = true;

  @property({ type: Boolean }) searching = false;

  @state() private propertyName = '';

  filterVariableValue: string = '';
  filterVariableName: string = '';
  filterVariableScope: string = '';
  isShowDefaultProps: boolean = false;

  @state() private _editingValueId: number | undefined;

  @state() draftNewVariableValue = '';

  @state() private draftPreviewTraces = new Map<
    string,
    PropertyResolutionTraceApiModel
  >();

  private draftPreviewGeneration = 0;

  private valueSelectionStart: number | null = null;

  private valueSelectionEnd: number | null = null;

  @state() tokenPopoverOpen = false;

  @state() tokenExplorerMode = false;

  @state() inlineDollarQuery = '';

  @state() tokenExplorerSearchQuery = '';

  @state() activeTokenCompletionIndex = 0;

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

      .env-variable-import-actions {
        margin-top: var(--lumo-space-s);
        margin-left: var(--lumo-space-xs);
        display: flex;
        align-items: center;
        gap: var(--lumo-space-xs);
      }

      .env-variable-add-actions {
        padding-top: 16px;
      }

      .value-resolver-stack {
        position: relative;
        display: flex;
        flex-wrap: wrap;
        gap: 8px;
        align-items: flex-end;
        width: 100%;
      }

      .token-explorer-suffix-btn {
        border: 1px solid var(--dorc-border-color, rgba(128, 128, 128, 0.35));
        background: var(--dorc-chip-bg, rgba(59, 130, 246, 0.14));
        color: var(--dorc-link-color, #2563eb);
        font-family: monospace;
        font-size: 0.72rem;
        font-weight: 700;
        border-radius: 4px;
        padding: 1px 5px;
        cursor: pointer;
        line-height: 1.3;
        margin-right: 2px;
      }

      .token-explorer-suffix-btn:hover:not(:disabled) {
        background: rgba(59, 130, 246, 0.24);
      }

      .token-explorer-suffix-btn:disabled {
        opacity: 0.45;
        cursor: default;
      }

      .token-intellisense-popover {
        position: absolute;
        top: calc(100% + 2px);
        left: 0;
        z-index: 1200;
        width: min(520px, 100%);
        max-height: 300px;
        display: flex;
        flex-direction: column;
        background: var(--lumo-base-color, #ffffff);
        border: 1px solid var(--dorc-border-color, rgba(128, 128, 128, 0.35));
        border-radius: 8px;
        box-shadow: 0 8px 24px rgba(0, 0, 0, 0.18);
        overflow: hidden;
      }

      .token-intellisense-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        padding: 6px 10px;
        background: var(--dorc-bg-secondary, rgba(128, 128, 128, 0.08));
        border-bottom: 1px solid
          var(--dorc-border-color, rgba(128, 128, 128, 0.2));
        font-size: var(--lumo-font-size-xxs, 0.72rem);
        color: var(--lumo-secondary-text-color);
      }

      .token-intellisense-search {
        flex: 1;
        border: 1px solid var(--dorc-border-color, rgba(128, 128, 128, 0.35));
        border-radius: 4px;
        padding: 3px 8px;
        font-size: var(--lumo-font-size-xs, 0.78rem);
        background: var(--lumo-base-color, #ffffff);
        color: var(--lumo-body-text-color, inherit);
      }

      .token-intellisense-list {
        overflow-y: auto;
        max-height: 240px;
      }

      .token-intellisense-item {
        display: flex;
        flex-direction: column;
        gap: 2px;
        padding: 6px 10px;
        cursor: pointer;
        border-bottom: 1px solid rgba(128, 128, 128, 0.1);
      }

      .token-intellisense-item:hover,
      .token-intellisense-item.active {
        background: rgba(59, 130, 246, 0.14);
      }

      .token-intellisense-item-top {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
      }

      .token-intellisense-name {
        font-family: monospace;
        font-size: var(--lumo-font-size-xs, 0.8rem);
        font-weight: 600;
        color: var(--lumo-body-text-color, inherit);
      }

      .token-intellisense-category {
        font-size: 0.68rem;
        padding: 1px 6px;
        border-radius: 999px;
        background: rgba(128, 128, 128, 0.14);
        color: var(--lumo-secondary-text-color);
        white-space: nowrap;
      }

      .token-intellisense-sample {
        font-size: var(--lumo-font-size-xs, 0.75rem);
        color: var(--lumo-secondary-text-color);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        max-width: 380px;
      }

      .dollar-token-segment-strip {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 6px;
        margin-top: 4px;
        padding: 5px 10px;
        border-radius: 6px;
        font-family: monospace;
        font-size: var(--lumo-font-size-xs, 0.78rem);
        background: rgba(59, 130, 246, 0.08);
        border: 1px solid rgba(59, 130, 246, 0.28);
      }

      .dollar-segment-literal {
        color: var(--lumo-body-text-color, inherit);
        white-space: pre-wrap;
      }

      .dollar-token-pill {
        display: inline-flex;
        align-items: center;
        gap: 5px;
        padding: 1px 7px;
        border-radius: 999px;
        font-family: monospace;
        font-size: 0.75rem;
        font-weight: 600;
        border: 1px solid transparent;
        cursor: help;
      }

      .dollar-token-pill-badge {
        font-size: 0.66rem;
        font-weight: 500;
        opacity: 0.88;
      }

      .dollar-token-pill--resolved {
        background: rgba(16, 185, 129, 0.16);
        border-color: rgba(16, 185, 129, 0.45);
        color: #047857;
      }

      .dollar-token-pill--fallback,
      .dollar-token-pill--runtime,
      .dollar-token-pill--warning {
        background: rgba(245, 158, 11, 0.16);
        border-color: rgba(245, 158, 11, 0.45);
        color: #b45309;
      }

      .dollar-token-pill--unresolved {
        background: rgba(239, 68, 68, 0.16);
        border-color: rgba(239, 68, 68, 0.45);
        color: #b91c1c;
      }

      .dollar-token-pill--masked {
        background: rgba(139, 92, 246, 0.16);
        border-color: rgba(139, 92, 246, 0.45);
        color: #6d28d9;
      }

      .dollar-token-pill--escaped {
        background: rgba(100, 116, 139, 0.16);
        border-color: rgba(100, 116, 139, 0.4);
        color: inherit;
      }

      .dollar-segment-arrow {
        color: var(--lumo-secondary-text-color);
        font-weight: 700;
      }

      .dollar-segment-resolved {
        color: var(--lumo-body-text-color, inherit);
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

      .dollar-lint-banner {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        margin-top: 4px;
        padding: 4px 10px;
        border-radius: 6px;
        font-size: var(--lumo-font-size-xs, 0.78rem);
        background: rgba(245, 158, 11, 0.14);
        border: 1px solid rgba(245, 158, 11, 0.35);
      }

      .dollar-lint-btn {
        border: none;
        background: transparent;
        color: var(--dorc-link-color);
        font-size: var(--lumo-font-size-xs, 0.78rem);
        font-weight: 600;
        cursor: pointer;
        text-decoration: underline;
        padding: 0;
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

        .env-variable-import-actions {
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

  /**
   * A stable class field, not an inline arrow in the template.
   *
   * Vaadin re-binds and CLEARS THE CACHE whenever the data provider's
   * identity changes (`_dataProviderChanged` -> `clearCache()`), and an
   * inline arrow is a new function on every host render. Once
   * `_editingValueId` became `@state()`, pressing Edit or Cancel re-rendered
   * the host and therefore threw the grid's cache away and re-queried the
   * API — a flash and a wasted paged request for what is a local UI toggle.
   */
  private variablesDataProvider = (
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
      const api = new RefDataScopedPropertyValuesApi(dorcApiConfiguration);
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
              new CustomEvent('searching-env-variables-finished', {
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
  };

  render() {
    return html`
      <dorc-spinner
        style="--dorc-spinner-z-index: 1000"
        ?hidden="${!(this.loading || this.searching)}"
      ></dorc-spinner>
      ${
        this.envLoaded
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
                          ${
                            this.loadingProperties
                              ? html`<div
                                  style="vertical-align: center"
                                  class="small-loader"
                                ></div> `
                              : html``
                          }
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
                      <tr>
                        <td></td>
                        <td style="padding-top: 8px;">
                          <div class="env-variable-import-actions">
                            <vaadin-button
                              @click="${this.downloadNonSecureVariables}"
                              ?disabled="${
                                !this.environmentName ||
                                this.exportingNonSecureProperties
                              }"
                              >Export Values</vaadin-button
                            >
                            <vaadin-button
                              @click="${this.openImportPropertiesDialog}"
                              ?disabled="${
                                !this.environment?.UserEditable ||
                                this.importingProperties
                              }"
                              >Import Values</vaadin-button
                            >
                            <input
                              id="import-environment-properties-csv"
                              type="file"
                              accept=".csv,text/csv"
                              style="display: none"
                              @change="${this.importPropertiesCsvSelected}"
                            />
                          </div>
                        </td>
                      </tr>
                    </table>
                    <table style="flex: 1; min-width: 400px">
                      <tr>
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
                              clear-button-visible
                              label="Value"
                              placeholder="Variable value..."
                              style="min-width: 320px; flex: 1 1 320px;"
                              helper-text="Type $ or Ctrl+Space for token IntelliSense; $$ escapes a literal $"
                              @keydown="${this._onNewVariableValueKeyDown}"
                              @blur="${(e: Event) => {
                                const target = e.currentTarget as TextField & {
                                  inputElement?: HTMLInputElement;
                                };
                                if (target?.inputElement) {
                                  this.valueSelectionStart =
                                    target.inputElement.selectionStart;
                                  this.valueSelectionEnd =
                                    target.inputElement.selectionEnd;
                                }
                              }}"
                              @input="${(e: Event) => {
                                const target = e.currentTarget as TextField & {
                                  inputElement?: HTMLInputElement;
                                };
                                this.handleNewVariableValueInput(target);
                              }}"
                              @value-changed="${(
                                e: CustomEvent<{ value?: string }>
                              ) => {
                                const nextVal = e.detail?.value ?? '';
                                if (nextVal !== this.draftNewVariableValue) {
                                  this.draftNewVariableValue = nextVal;
                                  this.updateInlineDollarTriggerFromValue(
                                    nextVal,
                                    nextVal.length
                                  );
                                  void this.refreshDraftValuePreview();
                                }
                              }}"
                            >
                              <button
                                type="button"
                                id="exploreDollarTokensBtn"
                                slot="suffix"
                                class="token-explorer-suffix-btn"
                                title="Explore $ tokens (or type $ / Ctrl+Space)"
                                aria-label="Explore $ tokens (or type $ / Ctrl+Space)"
                                @mousedown="${(e: MouseEvent) =>
                                  e.preventDefault()}"
                                @click="${(e: MouseEvent) => {
                                  e.stopPropagation();
                                  this.toggleTokenExplorerPopover();
                                }}"
                              >
                                $x$
                              </button>
                            </vaadin-text-field>
                            ${this.renderTokenIntellisensePopover()}
                          </div>
                          ${this.renderNewVariableValueHint()}
                        </td>
                        <td style="vertical-align: top;">
                          <div class="env-variable-add-actions">
                            <vaadin-button
                              @click="${this._addVariableValueClick}"
                              ?disabled="${!this.environment?.UserEditable}"
                              >Add Variable Value</vaadin-button
                            >
                          </div>
                        </td>
                        <td style="vertical-align: middle; min-width: 20px">
                          ${
                            this.addingVariableValue ||
                            this.importingProperties ||
                            this.exportingNonSecureProperties
                              ? html`<div
                                  style="vertical-align: middle"
                                  class="small-loader"
                                ></div> `
                              : html``
                          }
                        </td>
                      </tr>
                    </table>
                  </div>
                </vaadin-details>

                ${
                  this.environment?.EnvironmentSecure
                    ? html`<dismissible-item
                        key="secure-env-banner"
                        style="flex: 0 1 auto; width: 100%;"
                        .message="${this.secureEnvironmentMessage}"
                      ></dismissible-item>`
                    : html`<dismissible-item
                        key="non-secure-env-banner"
                        style="flex: 0 1 auto; width: 100%;"
                        .message="${this.secureMessage}"
                      ></dismissible-item>`
                }
                <vaadin-grid
                  id="grid"
                  column-reordering-allowed
                  multi-sort
                  theme="compact row-stripes no-row-borders no-border"
                  .dataProvider="${this.variablesDataProvider}"
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
          : html``
      }
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
    this.addEventListener(
      'variable-value-updated',
      this.variableValueUpdated as EventListener
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

  async downloadNonSecureVariables() {
    const environmentName = this.environmentName;

    if (!environmentName) {
      this.showImportError('Environment is not selected.');
      return;
    }

    const confirmed = await confirmPrompt(
      'Only non-secure values will be downloaded. Secure values are excluded.\n\nDo you want to continue?',
      {
        header: 'Download Values',
        confirmText: 'Download',
        confirmTheme: 'primary',
        cancelTheme: 'primary'
      }
    );

    if (!confirmed) {
      return;
    }

    this.exportingNonSecureProperties = true;
    try {
      const endpoint = `${dorcApiConfiguration.basePath}/PropertyValues/non-secure-export?environmentName=${encodeURIComponent(environmentName)}`;
      const headers: Record<string, string> = {};
      const accessToken = dorcApiConfiguration.accessToken?.('oauth2', [
        'dorc-api-np.manage'
      ]);
      if (accessToken) {
        headers.Authorization = accessToken;
      }

      const response = await fetch(endpoint, {
        method: 'GET',
        headers,
        credentials: 'include'
      });

      if (!response.ok) {
        throw new Error(
          `Download failed: ${response.status} ${response.statusText}`
        );
      }

      const blob = await response.blob();
      const contentDisposition = response.headers.get('content-disposition');
      const extractedFileName = contentDisposition
        ?.match(/filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i)
        ?.slice(1)
        .find(Boolean);

      if (!blob.size) {
        Notification.show(
          'No non-secure variables found for this environment.',
          {
            theme: 'contrast',
            position: 'bottom-start',
            duration: 5000
          }
        );
        return;
      }

      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = decodeURIComponent(
        extractedFileName ?? `${environmentName}-non-secure-variables.csv`
      );
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);

      this.showSuccessMessage('Non-secure variables downloaded successfully.');
    } catch (error) {
      this.showImportError(this.getErrorMessage(error));
    } finally {
      this.exportingNonSecureProperties = false;
    }
  }

  openImportPropertiesDialog() {
    const fileInput = this.shadowRoot?.querySelector(
      '#import-environment-properties-csv'
    ) as HTMLInputElement | null;
    fileInput?.click();
  }

  async importPropertiesCsvSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const environmentName = this.environmentName;

    if (!file) {
      return;
    }

    if (!environmentName) {
      this.showImportError('Environment is not selected.');
      input.value = '';
      return;
    }

    try {
      const content = await file.text();
      const parsedRows = this.parseImportedCsvRows(content);

      const confirmed = await confirmPrompt(
        `You are about to update ${parsedRows.length} value(s) for ${environmentName} env, proceed?`,
        {
          header: 'Import Values',
          confirmText: 'Proceed',
          confirmTheme: 'primary',
          cancelTheme: 'primary'
        }
      );

      if (!confirmed) {
        return;
      }

      await this.importPropertiesFromCsv(parsedRows);
    } catch (error) {
      this.showImportError(this.getErrorMessage(error));
    } finally {
      input.value = '';
    }
  }

  private async importPropertiesFromCsv(rows: ImportedPropertyCsvRow[]) {
    if (!this.environmentName) {
      this.showImportError('Environment is not selected.');
      return;
    }

    if (rows.length === 0) {
      this.showImportError('CSV file does not contain any property rows.');
      return;
    }

    this.importingProperties = true;

    try {
      const latestRowByProperty = new Map<string, ImportedPropertyCsvRow>();
      rows.forEach(row =>
        latestRowByProperty.set(row.propertyName.toLowerCase(), row)
      );
      const deduplicatedRows = Array.from(latestRowByProperty.values());

      const secureByProperty = new Map<string, boolean>();
      for (const row of deduplicatedRows) {
        const key = row.propertyName.toLowerCase();
        const existing = secureByProperty.get(key);
        if (existing !== undefined && existing !== row.isSecure) {
          this.showImportError(
            `Property "${row.propertyName}" has conflicting values for Is Secure in the CSV file.`
          );
          return;
        }
        secureByProperty.set(key, row.isSecure);
      }

      const propertiesApi = new PropertiesApi(dorcApiConfiguration);
      const propertyValuesApi = new PropertyValuesApi(dorcApiConfiguration);

      const existingProperties: PropertyApiModel[] =
        await this.resolveObservable(propertiesApi.propertiesGet());
      const existingPropertyMap = new Map<string, PropertyApiModel>();
      existingProperties.forEach((prop: PropertyApiModel) => {
        if (prop.Name) {
          existingPropertyMap.set(prop.Name.toLowerCase(), prop);
        }
      });

      const propertiesToCreate: PropertyApiModel[] = Array.from(
        secureByProperty.entries()
      )
        .filter(([key]) => !existingPropertyMap.has(key))
        .map(([key, secure]) => {
          const sourceRow = deduplicatedRows.find(
            r => r.propertyName.toLowerCase() === key
          );
          return {
            Name: sourceRow?.propertyName,
            Secure: secure
          };
        });

      const propertyResponses: Response[] = propertiesToCreate.length
        ? await this.resolveObservable(
            propertiesApi.propertiesPost({
              propertyApiModel: propertiesToCreate
            })
          )
        : [];

      let existingValues: PropertyValueDto[] = [];
      try {
        existingValues = await this.resolveObservable(
          propertyValuesApi.propertyValuesGet({
            environmentName: this.environmentName
          })
        );
      } catch (error: any) {
        if (error?.status !== 404) {
          throw error;
        }
      }

      const existingValuePropertySet = new Set(
        existingValues
          .map(v => v.Property?.Name?.toLowerCase())
          .filter((name): name is string => Boolean(name))
      );

      const effectiveSecureForProperty = (
        propertyName: string,
        csvSecure: boolean
      ) => {
        const existingProperty = existingPropertyMap.get(
          propertyName.toLowerCase()
        );
        if (existingProperty?.Secure !== undefined) {
          return existingProperty.Secure;
        }

        return csvSecure;
      };

      const toDto = (row: ImportedPropertyCsvRow): PropertyValueDto => ({
        Property: {
          Name: row.propertyName,
          Secure: effectiveSecureForProperty(row.propertyName, row.isSecure)
        },
        Value: row.value,
        PropertyValueFilter: this.environmentName
      });

      const createDtos = deduplicatedRows
        .filter(
          row => !existingValuePropertySet.has(row.propertyName.toLowerCase())
        )
        .map(toDto);

      const updateDtos = deduplicatedRows
        .filter(row =>
          existingValuePropertySet.has(row.propertyName.toLowerCase())
        )
        .map(toDto);

      const valueCreateResponses: Response[] = createDtos.length
        ? await this.resolveObservable(
            propertyValuesApi.propertyValuesPost({
              propertyValueDto: createDtos
            })
          )
        : [];

      const valueUpdateResponses: Response[] = updateDtos.length
        ? await this.resolveObservable(
            propertyValuesApi.propertyValuesPut({
              propertyValueDto: updateDtos
            })
          )
        : [];

      const allResponses = [
        ...propertyResponses,
        ...valueCreateResponses,
        ...valueUpdateResponses
      ];
      const failedResponses = allResponses.filter(
        response => response.Status?.toLowerCase() !== 'success'
      );

      if (failedResponses.length > 0) {
        this.errorAlert(failedResponses);
        Notification.show(
          `CSV import completed with ${failedResponses.length} error(s).`,
          {
            theme: 'contrast',
            position: 'bottom-start',
            duration: 5000
          }
        );
      } else {
        this.showSuccessMessage(
          `CSV import completed successfully. Imported ${deduplicatedRows.length} variable value(s).`
        );
      }

      this.grid?.clearCache();
      this.loading = true;
      this.getAllVariableNames();
    } finally {
      this.importingProperties = false;
    }
  }

  private parseImportedCsvRows(csvContent: string): ImportedPropertyCsvRow[] {
    const rows = csvContent
      .split(/\r?\n/)
      .map(line => line.trim())
      .filter(line => line !== '');

    if (rows.length === 0) {
      return [];
    }

    const parsedRows = rows.map(line => this.parseCsvLine(line));
    let dataStartIndex = 0;
    let propertyNameIndex = 0;
    let valueIndex = 1;
    let isSecureIndex = 2;

    const normalizedHeaders = parsedRows[0].map(header =>
      this.normalizeCsvHeader(header)
    );

    const hasHeader =
      (normalizedHeaders.indexOf('propertyname') >= 0 ||
        normalizedHeaders.indexOf('properyname') >= 0) &&
      normalizedHeaders.indexOf('value') >= 0;

    if (hasHeader) {
      dataStartIndex = 1;
      propertyNameIndex = normalizedHeaders.findIndex(
        header => header === 'propertyname' || header === 'properyname'
      );
      valueIndex = normalizedHeaders.findIndex(header => header === 'value');
      const secureHeaderIndex = normalizedHeaders.findIndex(
        header => header === 'issecure' || header === 'issecured'
      );
      if (secureHeaderIndex === -1) {
        throw new Error(
          "CSV header must include 'Is Secure' (or 'IsSecured') column."
        );
      }
      isSecureIndex = secureHeaderIndex;
    }

    if (propertyNameIndex === -1 || valueIndex === -1 || isSecureIndex === -1) {
      throw new Error(
        'CSV must provide PropertyName, Value and Is Secure columns.'
      );
    }

    return parsedRows.slice(dataStartIndex).map((columns, index) => {
      const rowNumber = index + dataStartIndex + 1;
      const propertyName = (columns[propertyNameIndex] ?? '').trim();
      const value = (columns[valueIndex] ?? '').trim();
      const rawSecure = (columns[isSecureIndex] ?? '').trim();

      if (!propertyName) {
        throw new Error(`PropertyName cannot be blank (row ${rowNumber}).`);
      }

      if (!value) {
        throw new Error(
          `Property value cannot be blank for '${propertyName}' (row ${rowNumber}).`
        );
      }

      return {
        propertyName,
        value,
        isSecure: this.parseCsvSecureValue(rawSecure, rowNumber)
      };
    });
  }

  private parseCsvLine(line: string): string[] {
    const columns: string[] = [];
    let current = '';
    let isInQuotes = false;

    for (let i = 0; i < line.length; i += 1) {
      const char = line[i];
      const next = line[i + 1];

      if (char === '"') {
        if (isInQuotes && next === '"') {
          current += '"';
          i += 1;
          continue;
        }

        isInQuotes = !isInQuotes;
        continue;
      }

      if (char === ',' && !isInQuotes) {
        columns.push(current);
        current = '';
        continue;
      }

      current += char;
    }

    columns.push(current);
    return columns;
  }

  private parseCsvSecureValue(value: string, rowNumber: number): boolean {
    const normalized = value.toLowerCase();
    if (normalized === 'true' || normalized === '1') {
      return true;
    }

    if (normalized === 'false' || normalized === '0') {
      return false;
    }

    throw new Error(
      `Invalid Is Secure value '${value}' on row ${rowNumber}. Use TRUE/FALSE or 1/0.`
    );
  }

  private normalizeCsvHeader(header: string): string {
    return header.toLowerCase().replace(/[^a-z]/g, '');
  }

  private async resolveObservable<T>(observable: Observable<T>): Promise<T> {
    return firstValueFrom(observable);
  }

  private showImportError(message: string) {
    const notification = new ErrorNotification();
    notification.setAttribute('errorMessage', message);
    this.shadowRoot?.appendChild(notification);
    notification.open();
  }

  private getErrorMessage(error: unknown): string {
    if (error instanceof Error) {
      return error.message;
    }

    if (typeof error === 'string') {
      return error;
    }

    return 'CSV import failed.';
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
    defaultPropertyResolutionClientCache.clear();
    if (this.grid) {
      this.grid.clearCache();
      this.loading = true;
    }
  }

  private variableValueUpdated() {
    defaultPropertyResolutionClientCache.clear();
    this._editingValueId = undefined;
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
    const propertiesApi = new PropertiesApi(dorcApiConfiguration);
    propertiesApi.propertiesGet().subscribe({
      next: (data: PropertyApiModel[]) => {
        this.properties = data.sort(this.sortProperties);
        this.loadingProperties = false;
      },
      error: (err: any) => {
        this.loadingProperties = false;
        this.errorAlert(err);
      },
      complete: () => console.log('done loading properties')
    });

    const api = new PropertyValuesApi(dorcApiConfiguration);
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
        error: (err: any) => {
          this.loadingScopeOptions = false;
          this.errorAlert(err);
        },
        complete: () => console.log('done loading variable value scope options')
      });
  }

  sortProperties(a: PropertyApiModel, b: PropertyApiModel): number {
    if (String(a.Name) > String(b.Name)) return 1;
    return -1;
  }

  public isSelectedPropertySecure(): boolean {
    const existingProperty = this.properties?.find(
      value => value.Name === this.propertyName
    );
    return Boolean(existingProperty?.Secure);
  }

  public async refreshDraftValuePreview() {
    const generation = ++this.draftPreviewGeneration;
    if (this.isSelectedPropertySecure() || !this.draftNewVariableValue) {
      this.draftPreviewTraces = new Map();
      return;
    }
    const referenced = parsePropertyTokens(
      this.draftNewVariableValue,
      'standard'
    )
      .filter(
        (t): t is Extract<typeof t, { kind: 'property' }> =>
          t.kind === 'property'
      )
      .map(t => t.propertyName);
    if (referenced.length === 0) {
      this.draftPreviewTraces = new Map();
      return;
    }
    try {
      const traces = await defaultPropertyResolutionClientCache.resolveProperties(
        { environmentName: this.environmentName || undefined },
        referenced
      );
      if (generation === this.draftPreviewGeneration) {
        this.draftPreviewTraces = traces;
      }
    } catch {
      if (generation === this.draftPreviewGeneration) {
        this.draftPreviewTraces = new Map();
      }
    }
  }

  private readCaretPosition(
    textField: (TextField & { inputElement?: HTMLInputElement }) | null
  ): { start: number; end: number } {
    const current = textField?.value ?? '';
    const innerInput =
      textField?.inputElement ??
      (textField?.querySelector('input') as HTMLInputElement | null) ??
      (textField?.shadowRoot?.querySelector('input') as HTMLInputElement | null);
    if (
      innerInput &&
      innerInput.value === current &&
      typeof innerInput.selectionStart === 'number'
    ) {
      return {
        start: innerInput.selectionStart,
        end:
          typeof innerInput.selectionEnd === 'number'
            ? innerInput.selectionEnd
            : innerInput.selectionStart
      };
    }
    if (
      current === this.draftNewVariableValue &&
      typeof this.valueSelectionStart === 'number' &&
      this.valueSelectionStart <= current.length
    ) {
      return {
        start: this.valueSelectionStart,
        end:
          typeof this.valueSelectionEnd === 'number'
            ? Math.min(this.valueSelectionEnd, current.length)
            : this.valueSelectionStart
      };
    }
    return { start: current.length, end: current.length };
  }

  private handleNewVariableValueInput(
    target: (TextField & { inputElement?: HTMLInputElement }) | null
  ) {
    if (!target) return;
    const nextVal = target.value ?? '';
    const innerInput =
      target.inputElement ??
      (target.querySelector('input') as HTMLInputElement | null) ??
      (target.shadowRoot?.querySelector('input') as HTMLInputElement | null);
    const caret =
      innerInput &&
      innerInput.value === nextVal &&
      typeof innerInput.selectionStart === 'number'
        ? innerInput.selectionStart
        : nextVal.length;
    this.valueSelectionStart = caret;
    this.valueSelectionEnd =
      innerInput &&
      innerInput.value === nextVal &&
      typeof innerInput.selectionEnd === 'number'
        ? innerInput.selectionEnd
        : caret;
    this.draftNewVariableValue = nextVal;
    this.updateInlineDollarTriggerFromValue(nextVal, caret);
    void this.refreshDraftValuePreview();
  }

  private updateInlineDollarTriggerFromValue(rawValue: string, caret: number) {
    if (this.isSelectedPropertySecure()) {
      this.tokenPopoverOpen = false;
      this.inlineDollarQuery = '';
      return;
    }
    const trigger = detectActiveDollarCompletion(rawValue, caret);
    if (trigger) {
      this.tokenPopoverOpen = true;
      this.tokenExplorerMode = false;
      this.inlineDollarQuery = trigger.queryText;
      this.activeTokenCompletionIndex = 0;
    } else if (!this.tokenExplorerMode) {
      this.tokenPopoverOpen = false;
      this.inlineDollarQuery = '';
    }
  }

  public openInlineTokenIntelliSense() {
    if (this.isSelectedPropertySecure()) return;
    const textField = this.shadowRoot?.querySelector(
      '#newVariableValue'
    ) as unknown as (TextField & { inputElement?: HTMLInputElement }) | null;
    const { start } = this.readCaretPosition(textField);
    const current = textField?.value ?? this.draftNewVariableValue;
    const trigger = detectActiveDollarCompletion(current, start);
    this.tokenPopoverOpen = true;
    this.tokenExplorerMode = false;
    this.inlineDollarQuery = trigger?.queryText ?? '';
    this.activeTokenCompletionIndex = 0;
  }

  public toggleTokenExplorerPopover() {
    if (this.isSelectedPropertySecure()) return;
    if (this.tokenPopoverOpen && this.tokenExplorerMode) {
      this.closeTokenPopover();
      return;
    }
    this.tokenPopoverOpen = true;
    this.tokenExplorerMode = true;
    this.tokenExplorerSearchQuery = '';
    this.activeTokenCompletionIndex = 0;
  }

  public closeTokenPopover() {
    this.tokenPopoverOpen = false;
    this.tokenExplorerMode = false;
    this.inlineDollarQuery = '';
    this.tokenExplorerSearchQuery = '';
    this.activeTokenCompletionIndex = 0;
  }

  public getFilteredCompletionTokens(): InsertableDollarTokenOption[] {
    const allTokens = buildInsertableDollarTokens(
      this.propertyValueScopeOptions,
      this.properties,
      this.propertyName
    );
    const query = this.tokenExplorerMode
      ? this.tokenExplorerSearchQuery
      : this.inlineDollarQuery;
    return filterInsertableDollarTokens(allTokens, query);
  }

  private _onNewVariableValueKeyDown = (e: KeyboardEvent) => {
    if (e.ctrlKey && (e.key === ' ' || e.code === 'Space')) {
      e.preventDefault();
      this.openInlineTokenIntelliSense();
      return;
    }
    if (!this.tokenPopoverOpen) return;
    const items = this.getFilteredCompletionTokens();
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      if (items.length > 0) {
        this.activeTokenCompletionIndex =
          (this.activeTokenCompletionIndex + 1) % items.length;
      }
      return;
    }
    if (e.key === 'ArrowUp') {
      e.preventDefault();
      if (items.length > 0) {
        this.activeTokenCompletionIndex =
          (this.activeTokenCompletionIndex - 1 + items.length) % items.length;
      }
      return;
    }
    if (e.key === 'Enter' || e.key === 'Tab') {
      const chosen = items[this.activeTokenCompletionIndex] ?? items[0];
      if (chosen) {
        e.preventDefault();
        this.insertTokenIntoValue(chosen.ValueOption);
      }
      return;
    }
    if (e.key === 'Escape') {
      e.preventDefault();
      this.closeTokenPopover();
    }
  };

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
    const { start, end } = this.readCaretPosition(textField);
    const activeTrigger = detectActiveDollarCompletion(current, start);
    const { nextValue: nextVal, nextCaretIndex: nextPos } =
      applyDollarTokenCompletion(current, token, activeTrigger, start, end);
    textField.value = nextVal;
    this.valueSelectionStart = nextPos;
    this.valueSelectionEnd = nextPos;
    this.closeTokenPopover();
    if (innerInput) {
      innerInput.value = nextVal;
      innerInput.focus();
      if (typeof innerInput.setSelectionRange === 'function') {
        innerInput.setSelectionRange(nextPos, nextPos);
      }
    } else if (typeof textField.focus === 'function') {
      textField.focus();
    }
    this.draftNewVariableValue = textField.value;
    void this.refreshDraftValuePreview();
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

  public escapeLiteralDollarsInValue() {
    if (this.isSelectedPropertySecure()) return;
    const textField = this.shadowRoot?.querySelector(
      '#newVariableValue'
    ) as unknown as TextField | null;
    if (!textField) return;
    const inspection = inspectUnescapedDollars(textField.value ?? '');
    textField.value = inspection.escapedSuggestion;
    this.draftNewVariableValue = inspection.escapedSuggestion;
    void this.refreshDraftValuePreview();
  }

  private renderTokenIntellisensePopover() {
    if (!this.tokenPopoverOpen || this.isSelectedPropertySecure()) {
      return html``;
    }
    const items = this.getFilteredCompletionTokens();
    return html`
      <div
        id="dollarTokenIntellisensePopover"
        class="token-intellisense-popover"
        role="listbox"
        aria-label="$ Token IntelliSense"
      >
        <div class="token-intellisense-header">
          ${this.tokenExplorerMode
            ? html`
                <input
                  id="tokenExplorerSearchInput"
                  class="token-intellisense-search"
                  type="text"
                  placeholder="Explore $ tokens by name, value, or category..."
                  .value="${this.tokenExplorerSearchQuery}"
                  @input="${(e: Event) => {
                    const input = e.currentTarget as HTMLInputElement;
                    this.tokenExplorerSearchQuery = input.value ?? '';
                    this.activeTokenCompletionIndex = 0;
                  }}"
                  @keydown="${this._onNewVariableValueKeyDown}"
                />
              `
            : html`
                <span
                  ><strong>$ Token IntelliSense</strong>
                  ${this.inlineDollarQuery
                    ? html`matching
                        <code
                          >${this.inlineDollarQuery
                            .toLowerCase()
                            .startsWith('fn:')
                            ? this.inlineDollarQuery
                            : `$${this.inlineDollarQuery}`}</code
                        >`
                    : html``}</span
                >
                <span>↑↓ navigate · Tab/Enter insert · Esc close</span>
              `}
          <button
            type="button"
            class="scope-chip-remove"
            aria-label="Close token popover"
            @mousedown="${(e: MouseEvent) => e.preventDefault()}"
            @click="${() => this.closeTokenPopover()}"
          >
            ×
          </button>
        </div>
        <div class="token-intellisense-list">
          ${items.length === 0
            ? html`<div
                style="padding: 8px 10px; font-size: var(--lumo-font-size-xs, 0.78rem); color: var(--lumo-secondary-text-color);"
              >
                No matching $ tokens in scope
              </div>`
            : items.map((item, idx) => {
                const sampleText = formatSampleResolvedPreview(
                  item.SampleResolvedValue
                );
                return html`
                  <div
                    class="token-intellisense-item ${idx ===
                    this.activeTokenCompletionIndex
                      ? 'active'
                      : ''}"
                    role="option"
                    tabindex="-1"
                    aria-selected="${idx === this.activeTokenCompletionIndex}"
                    data-token="${item.ValueOption}"
                    @mousedown="${(e: MouseEvent) => e.preventDefault()}"
                    @click="${() =>
                      this.insertTokenIntoValue(item.ValueOption)}"
                    @keydown="${(e: KeyboardEvent) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        this.insertTokenIntoValue(item.ValueOption);
                      }
                    }}"
                  >
                    <div class="token-intellisense-item-top">
                      <code class="token-intellisense-name"
                        >${item.ValueOption}</code
                      >
                      <span class="token-intellisense-category"
                        >${item.CategoryLabel}</span
                      >
                    </div>
                    <div
                      class="token-intellisense-sample"
                      title="${sampleText}"
                    >
                      ${sampleText}
                    </div>
                  </div>
                `;
              })}
        </div>
      </div>
    `;
  }

  public renderNewVariableValueHint() {
    if (this.isSelectedPropertySecure()) return html``;
    const inspection = inspectUnescapedDollars(this.draftNewVariableValue);
    const targetScope = this.environmentName || 'Current Environment';
    const effectiveTraces = synthesizeScopePreviewTraces(
      this.draftPreviewTraces,
      this.propertyValueScopeOptions,
      this.properties,
      targetScope
    );
    const preview = evaluateDollarValuePreview(
      this.draftNewVariableValue,
      effectiveTraces,
      targetScope
    );
    const segments = preview
      ? segmentizeDollarValueWithTraces(
          this.draftNewVariableValue,
          effectiveTraces
        )
      : [];

    return html`
      ${
        inspection.hasBraceSyntax
          ? html`<div class="dollar-lint-banner" role="status">
              <span
                >Variable values use <code>$VariableName$</code> rather than
                <code>\${VariableName}</code>.</span
              >
              <button
                type="button"
                id="convertBraceBtn"
                class="dollar-lint-btn"
                @click="${() => {
                  const textField = this.shadowRoot?.querySelector(
                    '#newVariableValue'
                  ) as unknown as TextField | null;
                  if (textField) {
                    textField.value = inspection.braceConvertedSuggestion;
                  }
                  this.draftNewVariableValue =
                    inspection.braceConvertedSuggestion;
                  void this.refreshDraftValuePreview();
                }}"
              >
                Convert to $Variable$
              </button>
            </div>`
          : inspection.hasUnclosedDollar
            ? html`<div class="dollar-lint-banner" role="status">
                <span
                  >${
                    inspection.hasOtherPropertyTokens
                      ? 'Unescaped $ (such as E$) will prevent other $Variable$ tokens in this value from expanding.'
                      : 'Contains a single $ (such as E$). Escape as $$ if you combine this path with $Variable$ tokens.'
                  }</span
                >
                <button
                  type="button"
                  id="escapeDollarBtn"
                  class="dollar-lint-btn"
                  @click="${() => this.escapeLiteralDollarsInValue()}"
                >
                  Escape literal $ as $$
                </button>
              </div>`
            : html``
      }
      ${
        preview
          ? html`<div
              id="dollarTokenSegmentStrip"
              class="dollar-token-segment-strip"
              title="${preview.scopeSummary}"
            >
              ${segments.map(seg => {
                if (seg.kind === 'literal') {
                  return html`<span class="dollar-segment-literal"
                    >${seg.displayText}</span
                  >`;
                }
                if (seg.kind === 'expression-prefix') {
                  return html`<span
                    class="dollar-token-pill dollar-token-pill--runtime"
                    data-token-status="expression-prefix"
                    title="${seg.tooltip}"
                    >${seg.displayText}</span
                  >`;
                }
                if (seg.kind === 'escaped-dollar') {
                  return html`<span
                    class="dollar-token-pill dollar-token-pill--escaped"
                    data-token-status="escaped-dollar"
                    title="${seg.tooltip}"
                    >$$ → $</span
                  >`;
                }
                if (seg.kind === 'unclosed-dollar') {
                  return html`<span
                    class="dollar-token-pill dollar-token-pill--warning"
                    data-token-status="unclosed-dollar"
                    title="${seg.tooltip}"
                    >${seg.displayText}</span
                  >`;
                }
                return html`<span
                  class="dollar-token-pill dollar-token-pill--${seg.status}"
                  data-token="${seg.rawToken}"
                  data-token-status="${seg.status}"
                  title="${seg.tooltip}"
                >
                  <span class="dollar-token-pill-name">${seg.rawToken}</span>
                  <span class="dollar-token-pill-badge">${seg.badgeLabel}</span>
                </span>`;
              })}
              <span class="dollar-segment-arrow" aria-hidden="true">→</span>
              <span class="dollar-segment-resolved"
                >Resolves (${targetScope}):
                <strong>${preview.resolvedText}</strong></span
              >
            </div>`
          : html``
      }
    `;
  }

  _propNameValueChanged(data: CustomEvent) {
    if (data) {
      const combo = data.target as ComboBox;
      this.propertyName = combo.value;
      void this.refreshDraftValuePreview();
    }
  }

  _addVariableValueClick() {
    const textField = this.shadowRoot?.querySelector(
      '#newVariableValue'
    ) as unknown as TextField;
    const api = new PropertyValuesApi(dorcApiConfiguration);
    const existingProperty = this.properties?.find(
      value => value.Name === this.propertyName
    );
    if (existingProperty) {
      this.addingVariableValue = true;
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
              defaultPropertyResolutionClientCache.clear();
              value.forEach((response: Response) => {
                console.log(response.Status);
              });
              if (textField) textField.value = '';
              this.draftNewVariableValue = '';
              this.draftPreviewTraces = new Map();
              this.valueSelectionStart = null;
              this.valueSelectionEnd = null;
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

  variableValueControlsRenderer = (item: FlatPropertyValueApiModel) => {
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
                  new CustomEvent('searching-env-variables-started', {
                    detail: {
                      field: variableIsShowDefaultProps,
                      value: e.target.checked
                    },
                    bubbles: true,
                    composed: true
                  })
                );
              }}"
            >
              <label slot="label" title="Show default property values also"
                >Show Defaults</label
              >
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
