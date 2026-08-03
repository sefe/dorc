import { live } from 'lit/directives/live.js';
import { ref } from 'lit/directives/ref.js';
import { columnBodyRenderer, columnHeaderRenderer } from '@vaadin/grid/lit';
import { css, PropertyValues } from 'lit';
import '../components/dorc-spinner';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid-filter';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/checkbox';
import '@vaadin/combo-box';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '../components/add-daemon';
import '@vaadin/text-field';
import { Grid, GridDataProviderCallback, GridDataProviderParams, GridFilterDefinition, GridSorterDefinition } from '@vaadin/grid';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import { PagedDataSorting, PowerShellVersionDto, PowerShellVersionsApi, RefDataScriptsApi, ScriptApiModel } from '../apis/dorc-api';
import { map } from 'lit/directives/map.js';
import { GetScriptsListResponseDto, PagedDataFilter } from '../apis/dorc-api';
import GlobalCache from '../global-cache';
import '../components/hegs-json-viewer';
import { HegsJsonViewer } from '../components/hegs-json-viewer';
import '@vaadin/grid/vaadin-grid-sorter';

const variableName = 'Name';
const variablePath = 'Path';
const variableProjectNames = 'ProjectNames';

/**
 * Puts a row's JSON into a `hegs-json-viewer` and expands it.
 *
 * The viewer seeds `.data` from its own text exactly once, in
 * connectedCallback. Grid cells are recycled — Vaadin only clears a cell when
 * the renderer function itself changes — so the element survives into the next
 * row and would keep showing the first row's JSON even though its text had
 * been updated. Binding `.data` per render fixes that; returning a fresh
 * callback each time is what makes `ref` re-run, since expansion is only
 * exposed as a method.
 */
const showJson =
  (raw: string | null | undefined) =>
  (element?: Element) => {
    if (!element) return;
    const viewer = element as unknown as HegsJsonViewer & { data?: unknown };
    viewer.data = parseJson(raw);
    viewer.expand('**');
  };

/** Malformed JSON renders as the raw string rather than blowing up the grid. */
function parseJson(raw: string | null | undefined): unknown {
  if (!raw) return {};
  try {
    return JSON.parse(raw);
  } catch {
    return raw;
  }
}

@customElement('page-scripts-list')
export class PageScriptsList extends ResponsiveMixin(PageElement) {
  @property({ type: Array }) scripts: Array<ScriptApiModel> = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  // Reactive so the dependency arrays that name it are real: the two editable
  // columns gate on it through canEditScripts(), and an undecorated field
  // cannot drive the update they need.
  @state() public userRoles: string[] = [];

  @property({ type: Boolean }) private loading = true;

  @property({ type: Boolean }) searching = false;

  @property({ type: Boolean }) private rolesLoading = true;

  @property({ type: Array }) private powerShellVersions: string[] = [];

  @query('#grid') grid: Grid | undefined;

  variableName: string =
    new URLSearchParams(location.search).get('search-name') ?? '';
  variablePath: string =
    new URLSearchParams(location.search).get('search-path') ?? '';
  variableProjectNames: string =
    new URLSearchParams(location.search).get('search-project') ?? '';

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
        height: 100%;
      }
      .project-tag {
        font-size: var(--lumo-font-size-s);
        border: 0;
        font-family: monospace;
        background-color: var(
          --_lumo-button-background-color,
          var(--lumo-contrast-5pct)
        );
        color: var(--lumo-secondary-text-color);
        display: inline-block;
        padding: 3px;
        margin: 3px;
        text-decoration: none;
        border-radius: 3px;
      }

      .project-tag:hover {
        background-color: var(
          --_lumo-button-background-color,
          var(--lumo-contrast-10pct)
        );
        cursor: pointer;
        text-decoration: none;
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
      <dorc-spinner style="--dorc-spinner-z-index: 1000" ?hidden="${!(this.loading || this.searching || this.rolesLoading)}"></dorc-spinner>

      ${this.rolesLoading
        ? html``
        : html`<vaadin-grid
            id="grid"
            column-reordering-allowed
            multi-sort
            ?hidden="${this.loading}"
            theme="compact row-stripes no-row-borders no-border"
            .dataProvider="${(
              params: GridDataProviderParams<ScriptApiModel>,
              callback: GridDataProviderCallback<ScriptApiModel>
            ) => {
              if (this.variableName !== '' && this.variableName !== undefined) {
                params.filters.push({
                  path: variableName,
                  value: this.variableName
                });
              }

              if (this.variablePath !== '' && this.variablePath !== undefined) {
                params.filters.push({
                  path: variablePath,
                  value: this.variablePath
                });
              }

              if (this.variableProjectNames !== '' && this.variableProjectNames !== undefined) {
                params.filters.push({
                  path: variableProjectNames,
                  value: this.variableProjectNames
                });
              }

              const api = new RefDataScriptsApi();
              api
                .refDataScriptsPut({
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
                  next: (data: GetScriptsListResponseDto) => {
                    this.dispatchEvent(
                      new CustomEvent('searching-scripts-finished', {
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
                      new CustomEvent('scripts-loaded', {
                        detail: {},
                        bubbles: true,
                        composed: true
                      })
                    );
                    console.log(
                      `done loading scripts page:${+(params.page + Number(1))}`
                    );
                  }
                });
            }}"
            style="z-index: 100;"
          >
            <vaadin-grid-column
              header="Enabled"
              resizable
              width="80px"
              flex-grow="0"
              ${columnBodyRenderer(this.enabledRenderer, [this.userRoles])}
            >
            </vaadin-grid-column>
            <vaadin-grid-column
              path="Name"
              header="Script Name"
              resizable
              ${columnHeaderRenderer(this.nameHeaderRenderer, [])}
              width="500px"
              flex-grow="0"
            >
            </vaadin-grid-column>
            <vaadin-grid-column
              path="ProjectNames"
              header="Projects"
              resizable
              width="200px"
              flex-grow="0"
              ${columnBodyRenderer(this.projectNamesRenderer, [])}
              ${columnHeaderRenderer(this.projectNamesHeaderRenderer, [])}
              ?hidden="${this._narrowScreen}"
            >
            </vaadin-grid-column>
            <vaadin-grid-sort-column
              path="NonProdOnly"
              header="Non Prod Only"
              resizable
              width="150px"
              flex-grow="0"
              ${columnBodyRenderer(this.nonProdRenderer, [])}
              ?hidden="${this._narrowScreen}"
            ></vaadin-grid-sort-column>
            <vaadin-grid-column
              path="Path"
              header="Path"
              resizable
              ${columnBodyRenderer(this._jsonRenderer, [])}
              ${columnHeaderRenderer(this.pathHeaderRenderer, [])}
              ?hidden="${this._narrowScreen}"
            ></vaadin-grid-column>
            <vaadin-grid-column
              path="PowerShellVersionNumber"
              header="PS Version"
              resizable
              ${columnBodyRenderer(this.psVersionRenderer, [
                this.powerShellVersions,
                this.userRoles
              ])}
              ?hidden="${this._narrowScreen}"
            ></vaadin-grid-column>
          </vaadin-grid>`}
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'scripts-loaded',
      this.scriptsLoaded as EventListener
    );

    this.addEventListener(
      'searching-scripts-started',
      this.searchingScriptsStarted as EventListener
    );

    this.addEventListener(
      'searching-scripts-finished',
      this.searchingScriptsFinished as EventListener
    );
  }

  constructor() {
    super();
    this.loadPowerShellVersions();
    this.getUserRoles();
    this.rolesLoading = false;
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

  private searchingScriptsFinished() {
    this.searching = false;
  }

  
  private projectNamesRenderer = (
      item: ScriptApiModel
    ) => {
      const script = item;
      const projectNames = script.ProjectNames ?? [];

      return html`
          ${map(
            projectNames,
            value =>
              html` <button
                class="project-tag"
                @click="${() =>
                          this.dispatchEvent(
                                  new CustomEvent('open-project-envs', {
                                      detail: {
                                          Project: {
                                            ProjectName: value
                                          }
                                      },
                                      bubbles: true,
                                      composed: true
                                  })
                          )}"
              >
                ${value}
              </button>`
          )}
        `;
    };

  nonProdRenderer(script: ScriptApiModel) {
    return html`<vaadin-checkbox
      disabled
      .checked="${script.NonProdOnly as boolean}"
    ></vaadin-checkbox>`;
  }

  enabledRenderer(script: ScriptApiModel) {
    return html`<vaadin-checkbox
      ?disabled="${!this.canEditScripts()}"
      .checked="${live(script.IsEnabled as boolean)}"
      @checked-changed="${(e: CustomEvent) => {
        // Don't fire when the value is unchanged.
        if (script.IsEnabled === e.detail.value) return;
        script.IsEnabled = e.detail.value;
        this.saveScript(script, `IsEnabled set to ${script.IsEnabled}`);
      }}"
    ></vaadin-checkbox>`;
  }

  private psVersionRenderer(script: ScriptApiModel) {
    return html`<vaadin-combo-box
      .items="${this.powerShellVersions}"
      .value="${live(script.PowerShellVersionNumber ?? '')}"
      ?disabled="${!this.canEditScripts()}"
      @value-changed="${(e: CustomEvent) => {
        const value = e.detail.value;
        if (!value || script.PowerShellVersionNumber === value) return;
        script.PowerShellVersionNumber = value;
        this.saveScript(script, `PowerShellVersionNumber set to ${value}`);
      }}"
    ></vaadin-combo-box>`;
  }

  /** Admins and power users may edit scripts; everyone else reads them. */
  private canEditScripts() {
    return Boolean(
      this.userRoles?.some(role => role === 'Admin' || role === 'PowerUser')
    );
  }

  private saveScript(script: ScriptApiModel, what: string) {
    new RefDataScriptsApi()
      .refDataScriptsEditPut({ scriptApiModel: script })
      .subscribe({
        next: (data: boolean) =>
          console.log(`script with id ${script.Id} ${what} result ${data}`)
      });
  }

  _jsonRenderer(script: ScriptApiModel) {
    if (!script.IsPathJSON) {
      return html`<div>${script.Path}</div>`;
    }
    // The viewer is configured through a method, so `ref` fires exactly when
    // the element exists rather than querying it back out of the cell.
    return html`<hegs-json-viewer
      style="font-size: small"
      ${ref(showJson(script.Path))}
    ></hegs-json-viewer>`;
  }

  private searchingScriptsStarted(event: CustomEvent) {
    if (event.detail.value !== undefined) {
      this.debouncedInputHandler(event.detail.field, event.detail.value);
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

  private debouncedInputHandler = this.debounce(
    (field: string, value: string) => {
      switch (field) {
        case variableName:
          this.variableName = value;
          break;
        case variablePath:
          this.variablePath = value;
          break;
        case variableProjectNames:
          this.variableProjectNames = value;
          break;
        default:
          break;
      }
      this.grid?.clearCache();
      this.searching = true;
    },
    400 // debounce wait time
  );

  nameHeaderRenderer() {
    return html`
        <vaadin-grid-sorter
          path="Name"
          style="align-items: normal"
        ></vaadin-grid-sorter>
        <vaadin-text-field
          placeholder="Name"
          clear-button-visible
          focus-target
          style="width: 200px"
          theme="small"
          value="${this.variableName}"
          @input="${(e: InputEvent) => {
            const textField = e.target as HTMLInputElement;

            this.dispatchEvent(
              new CustomEvent('searching-scripts-started', {
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

  pathHeaderRenderer() {
    return html`
        <vaadin-grid-sorter
          path="Path"
          style="align-items: normal"
        ></vaadin-grid-sorter>
        <vaadin-text-field
          placeholder="Path"
          clear-button-visible
          focus-target
          style="width: 200px"
          theme="small"
          value="${this.variablePath}"
          @input="${(e: InputEvent) => {
            const textField = e.target as HTMLInputElement;

            this.dispatchEvent(
              new CustomEvent('searching-scripts-started', {
                detail: {
                  field: variablePath,
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

  projectNamesHeaderRenderer() {
    return html`
        <div style="display: flex; flex-direction: column;">
          <vaadin-text-field
            placeholder="Project"
            clear-button-visible
            focus-target
            style="width: 180px"
            theme="small"
            value="${this.variableProjectNames}"
            @input="${(e: InputEvent) => {
              const textField = e.target as HTMLInputElement;

              this.dispatchEvent(
                new CustomEvent('searching-scripts-started', {
                  detail: {
                    field: variableProjectNames,
                    value: textField?.value
                  },
                  bubbles: true,
                  composed: true
                })
              );
            }}"
          ></vaadin-text-field>
        </div>
      `;
  }

  private scriptsLoaded() {
    this.loading = false;
  }

  private loadPowerShellVersions() {
    const api = new PowerShellVersionsApi();
    api.powerShellVersionsGet().subscribe({
      next: (versions: PowerShellVersionDto[]) => {
        this.powerShellVersions = versions.map(v => v.Value || '').filter(v => v !== '');
      },
      error: (error) => {
        console.error('Failed to load PowerShell versions:', error);
        // Fallback to hardcoded values
        this.powerShellVersions = ['v5.1', 'v7'];
      }
    });
  }
}