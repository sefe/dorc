import type { Checkbox } from '@vaadin/checkbox';
import type { ComboBox } from '@vaadin/combo-box';
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
import {
  Grid,
  GridDataProviderCallback,
  GridDataProviderParams,
  GridFilterDefinition,
  GridSorterDefinition
} from '@vaadin/grid';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import {
  PagedDataSorting,
  PowerShellVersionDto,
  PowerShellVersionsApi,
  RefDataScriptsApi,
  ScriptApiModel
} from '../apis/dorc-api';
import { map } from 'lit/directives/map.js';
import { GetScriptsListResponseDto, PagedDataFilter } from '../apis/dorc-api';
import GlobalCache from '../global-cache';
import '../components/hegs-json-viewer';
import { HegsJsonViewer } from '../components/hegs-json-viewer';
import '@vaadin/grid/vaadin-grid-sorter';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { Notification } from '@vaadin/notification';
import { confirmPrompt } from '../components/confirm-prompt';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';

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
const showJson = (raw: string | null | undefined) => (element?: Element) => {
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
  @state() private editing = false;
  @state() private error = '';

  @property({ type: Boolean }) loading = true;

  @property({ type: Boolean }) searching = false;

  @property({ type: Boolean }) rolesLoading = true;

  @property({ type: Array }) powerShellVersions: string[] = [];

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
        flex-direction: column;
        width: 100%;
        overflow: hidden;
        height: 100%;
      }
      /* Same density as the Monitor grid: fill the remaining column height
         below the banner instead of 100% of the host, which pushed the last
         rows under the bottom edge, and drop the vertical cell padding. */
      vaadin-grid#grid {
        --divider-color: var(--dorc-border-color);
        flex: 1;
        min-height: 0;
      }
      vaadin-grid-cell-content {
        padding-top: 0;
        padding-bottom: 0;
        margin: 0;
      }
      /* The Path JSON used to render at "small" inside a column that shared
         its width with PS Version, so it scrolled sideways in a few lines of
         tiny text. Give it the cell width and the grid's own text size. */
      .path-json {
        display: block;
        width: 100%;
        margin: var(--lumo-space-xs) 0;
        font-size: var(--lumo-font-size-s);
        line-height: var(--lumo-line-height-s);
        white-space: normal;
        overflow-wrap: anywhere;
        --indent-size: 1em;
      }
      .definitions-link {
        padding: var(--lumo-space-s);
        color: var(--dorc-text-secondary-strong);
      }
      .definitions-link a {
        color: var(--dorc-link-color);
      }
      .error {
        padding: var(--lumo-space-s);
        color: var(--lumo-error-text-color);
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

  // A stable identity, not an inline arrow. Vaadin's `_dataProviderChanged`
  // calls `clearCache()` whenever the provider's identity changes, so a new
  // closure per render makes the grid drop its pages and re-query. That went
  // from harmless to visible when `userRoles` became `@state`: for a non-admin
  // the roles callback used to change nothing reactive, and now it re-renders
  // the host — so the grid populates, blanks, and re-queries once roles land.
  // Same fix, same reason, as `env-variables.ts`.
  private scriptsDataProvider = (
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

    if (
      this.variableProjectNames !== '' &&
      this.variableProjectNames !== undefined
    ) {
      params.filters.push({
        path: variableProjectNames,
        value: this.variableProjectNames
      });
    }

    const api = new RefDataScriptsApi(dorcApiConfiguration);
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
  };

  render() {
    return html`
      <div class="definitions-link">
        <a href="/deployment-definitions">Deployment definitions</a> includes
        PowerShell and all Terraform sources. This legacy view also retains
        scripts without a component.
      </div>
      ${this.error ? html`<div class="error" role="alert">${this.error}</div>` : ''}
      <dorc-spinner
        style="--dorc-spinner-z-index: 1000"
        ?hidden="${!(this.loading || this.searching || this.rolesLoading)}"
      ></dorc-spinner>

      ${
        this.rolesLoading
          ? html``
          : html`<vaadin-grid
              id="grid"
              column-reordering-allowed
              multi-sort
              ?hidden="${this.loading}"
              theme="compact row-stripes no-row-borders no-border"
              .dataProvider="${this.scriptsDataProvider}"
              style="z-index: 100;"
            >
              <vaadin-grid-column
                header="Enabled"
                resizable
                width="80px"
                flex-grow="0"
                ${columnBodyRenderer(this.enabledRenderer, [this.userRoles, this.editing])}
              >
              </vaadin-grid-column>
              <vaadin-grid-column
                path="Name"
                header="Script Name"
                resizable
                ${columnHeaderRenderer(this.nameHeaderRenderer, [])}
                width="260px"
                flex-grow="1"
              >
              </vaadin-grid-column>
              <vaadin-grid-column
                path="ProjectNames"
                header="Projects"
                resizable
                width="180px"
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
                width="130px"
                flex-grow="0"
                ${columnBodyRenderer(this.nonProdRenderer, [])}
                ?hidden="${this._narrowScreen}"
              ></vaadin-grid-sort-column>
              <vaadin-grid-column
                path="Path"
                header="Path"
                resizable
                width="320px"
                flex-grow="2"
                ${columnBodyRenderer(this._jsonRenderer, [])}
                ${columnHeaderRenderer(this.pathHeaderRenderer, [])}
                ?hidden="${this._narrowScreen}"
              ></vaadin-grid-column>
              <vaadin-grid-column
                path="PowerShellVersionNumber"
                header="PS Version"
                resizable
                width="140px"
                flex-grow="0"
                ${columnBodyRenderer(this.psVersionRenderer, [
                  this.powerShellVersions,
                  this.userRoles,
                  this.editing
                ])}
                ?hidden="${this._narrowScreen}"
              ></vaadin-grid-column>
            </vaadin-grid>`
      }
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
    this.isPowerUser =
      this.userRoles.find(p => p === 'PowerUser') !== undefined;
  }

  private searchingScriptsFinished() {
    this.searching = false;
  }

  private projectNamesRenderer = (item: ScriptApiModel) => {
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

  // `change` rather than `checked-changed`: the latter is a notify event that
  // also fires when Lit commits the property. Grid cells are recycled, so
  // committing the next row's value fires it into the previous row's listener,
  // which is still attached at that point — and this handler persists what it
  // is given. `change` only fires on a user gesture.
  enabledRenderer(script: ScriptApiModel) {
    return html`<vaadin-checkbox
      ?disabled="${!this.canEditScripts() || this.editing || script.CanEdit === false}"
      .checked="${live(script.IsEnabled as boolean)}"
      @change="${(e: Event) => {
        const checked = (e.currentTarget as Checkbox).checked;
        if (script.IsEnabled === checked) return;
        void this.saveScript({ ...script, IsEnabled: checked });
      }}"
    ></vaadin-checkbox>`;
  }

  private psVersionRenderer(script: ScriptApiModel) {
    return html`<vaadin-combo-box
      .items="${this.powerShellVersions}"
      .value="${live(script.PowerShellVersionNumber ?? '')}"
      ?disabled="${!this.canEditScripts() || this.editing || script.CanEdit === false}"
      @change="${(e: Event) => {
        const value = (e.currentTarget as ComboBox).value;
        if (!value || script.PowerShellVersionNumber === value) return;
        void this.saveScript({ ...script, PowerShellVersionNumber: value });
      }}"
    ></vaadin-combo-box>`;
  }

  /** Admins and power users may edit scripts; everyone else reads them. */
  private canEditScripts() {
    return Boolean(
      this.userRoles?.some(role => role === 'Admin' || role === 'PowerUser')
    );
  }

  private async saveScript(script: ScriptApiModel) {
    if (this.editing || !this.canEditScripts()) return;
    const grid = this.grid;
    this.editing = true;
    this.error = '';
    const projects = script.ProjectNames?.join(', ') || 'No project consumers';
    if (
      !(await confirmPrompt(
        `Update ${script.Name} globally?\n\nAffected projects: ${projects}.\n\nEvery consuming definition is revised together. Existing submitted requests retain their original configuration.`,
        {
          header: 'Shared script impact',
          confirmText: 'Confirm shared edit',
          confirmTheme: 'primary'
        }
      ))
    ) {
      this.editing = false;
      grid?.clearCache();
      return;
    }
    new RefDataScriptsApi(dorcApiConfiguration)
      .refDataScriptsEditPut({
        scriptApiModel: { ...script, ConfirmImpact: true }
      })
      .subscribe({
        next: (data: boolean) => {
          this.editing = false;
          grid?.clearCache();
          if (!data) {
            this.error =
              'The script update was not accepted. Reload and review the current revision.';
            return;
          }
          Notification.show(
            'Shared script updated and definition revisions recorded.',
            { theme: 'success', position: 'bottom-end', duration: 5000 }
          );
        },
        error: error => {
          this.editing = false;
          this.error =
            retrieveErrorMessage(error) ||
            'Could not save the shared script. Reload to review its revision and affected projects.';
          grid?.clearCache();
        }
      });
  }

  _jsonRenderer(script: ScriptApiModel) {
    if (!script.IsPathJSON) {
      return html`<div>${script.Path}</div>`;
    }
    // The viewer is configured through a method, so `ref` fires exactly when
    // the element exists rather than querying it back out of the cell.
    return html`<hegs-json-viewer
      class="path-json"
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
    const api = new PowerShellVersionsApi(dorcApiConfiguration);
    api.powerShellVersionsGet().subscribe({
      next: (versions: PowerShellVersionDto[]) => {
        this.powerShellVersions = versions
          .map(v => v.Value || '')
          .filter(v => v !== '');
      },
      error: error => {
        console.error('Failed to load PowerShell versions:', error);
        // Fallback to hardcoded values
        this.powerShellVersions = ['v5.1', 'v7'];
      }
    });
  }
}
