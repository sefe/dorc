import { css, PropertyValues, render } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid-filter';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/checkbox';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '../components/add-daemon';
import '@polymer/paper-dialog';
import '@vaadin/text-field';
import {
  Grid,
  GridDataProviderCallback,
  GridDataProviderParams,
  GridFilterDefinition,
  GridItemModel,
  GridSorterDefinition
} from '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Checkbox } from '@vaadin/checkbox';
import { PageElement } from '../helpers/page-element';
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
import { ComboBox } from '@vaadin/combo-box';

const variableName = 'Name';
const variablePath = 'Path';
const variableProjectNames = 'ProjectNames';

@customElement('page-scripts-list')
export class PageScriptsList extends PageElement {
  @property({ type: Array }) scripts: Array<ScriptApiModel> = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  public userRoles!: string[];

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
        --divider-color: rgb(223, 232, 239);
        height: 100%;
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
      .project-tag {
        font-size: 14px;
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
    `;
  }

  render() {
    return html`
      <div
        class="overlay"
        style="z-index: 1000"
        ?hidden="${!(this.loading || this.searching || this.rolesLoading)}"
      >
        <div class="overlay__inner">
          <div class="overlay__content">
            <span class="spinner"></span>
          </div>
        </div>
      </div>

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

              if (
                this.variableProjectNames !== '' &&
                this.variableProjectNames !== undefined
              ) {
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
              .renderer="${this.enabledRenderer.bind(this)}"
            >
            </vaadin-grid-column>
            <vaadin-grid-column
              path="Name"
              header="Script Name"
              resizable
              .headerRenderer="${this.nameHeaderRenderer.bind(this)}"
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
              .renderer="${this.projectNamesRenderer.bind(this)}"
              .headerRenderer="${this.projectNamesHeaderRenderer.bind(this)}"
            >
            </vaadin-grid-column>
            <vaadin-grid-sort-column
              path="NonProdOnly"
              header="Non Prod Only"
              resizable
              width="150px"
              flex-grow="0"
              .renderer="${this.nonProdRenderer}"
            ></vaadin-grid-sort-column>
            <vaadin-grid-column
              path="Path"
              header="Path"
              resizable
              .renderer="${this._jsonRenderer}"
              .headerRenderer="${this.pathHeaderRenderer}"
            ></vaadin-grid-column>
            <vaadin-grid-column
              path="PowerShellVersionNumber"
              header="PS Version"
              resizable
              .renderer="${this.psVersionRenderer.bind(this)}"
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
    this.isPowerUser =
      this.userRoles.find(p => p === 'PowerUser') !== undefined;
  }

  private searchingScriptsFinished() {
    this.searching = false;
  }

  private psVersionRenderer(root: HTMLElement, _: any, rowData: any) {
    const script = rowData.item as ScriptApiModel;
    const select = new ComboBox();
    select.items = this.powerShellVersions;
    select.value = script.PowerShellVersionNumber ?? '';

    select.disabled = !this.isAdmin && !this.isPowerUser;

    select.addEventListener('value-changed', (event: any) => {
      if (
        script.PowerShellVersionNumber != event.detail.value &&
        !!event.detail.value
      ) {
        script.PowerShellVersionNumber = event.detail.value;
        const api = new RefDataScriptsApi();
        api.refDataScriptsEditPut({ scriptApiModel: script }).subscribe({
          next: (data: boolean) =>
            console.log(
              `script with id ${script.Id} PowerShellVersionNumber set to ${
                event.detail.value
              } result ${data}`
            )
        });
      }
    });
    render(select, root);
  }

  private projectNamesRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<ScriptApiModel>
  ) => {
    const script = model.item;
    const projectNames = script.ProjectNames ?? [];

    render(
      html`
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
      `,
      root
    );
  };

  nonProdRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<ScriptApiModel>
  ) {
    const script = model.item as ScriptApiModel;

    const checkbox = new Checkbox();

    checkbox.checked = script.NonProdOnly as boolean;
    checkbox.disabled = true;

    render(checkbox, root);
  }

  enabledRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<ScriptApiModel>
  ) {
    const script = model.item as ScriptApiModel;

    let isAdmin: boolean;
    if (
      this.userRoles &&
      this.userRoles.find((p: string) => p === 'Admin') !== undefined
    ) {
      isAdmin = true;
    } else {
      isAdmin = false;
    }

    let isPowerUser: boolean;
    if (
      this.userRoles &&
      this.userRoles.find((p: string) => p === 'PowerUser') !== undefined
    ) {
      isPowerUser = true;
    } else {
      isPowerUser = false;
    }

    const checkbox = new Checkbox();

    checkbox.checked = script.IsEnabled as boolean;
    checkbox.disabled = !(isAdmin || isPowerUser);

    checkbox.addEventListener('checked-changed', (e: CustomEvent) => {
      if (script.IsEnabled !== e.detail.value) {
        // don't fire when value is same
        script.IsEnabled = e.detail.value;
        const api = new RefDataScriptsApi();
        api.refDataScriptsEditPut({ scriptApiModel: script }).subscribe({
          next: (data: boolean) =>
            console.log(
              `script with id ${script.Id} IsEnabled set to ${
                script.IsEnabled
              } result ${data}`
            )
        });
      }
    });

    render(checkbox, root);
  }

  _jsonRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<ScriptApiModel>
  ) {
    const script = model.item as ScriptApiModel;

    if (script.IsPathJSON) {
      root.innerHTML = `<hegs-json-viewer style="font-size: small ">${
        script.Path
      }</hegs-json-viewer>`;
      const viewer = root.querySelector(
        'hegs-json-viewer'
      ) as unknown as HegsJsonViewer;
      viewer.expand('**');
    } else {
      root.innerHTML = `<div>${script.Path}</div>`;
    }
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

  nameHeaderRenderer(root: HTMLElement) {
    render(
      html`
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
      `,
      root
    );
  }

  pathHeaderRenderer(root: HTMLElement) {
    render(
      html`
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
      `,
      root
    );
  }

  projectNamesHeaderRenderer(root: HTMLElement) {
    render(
      html`
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
      `,
      root
    );
  }

  private scriptsLoaded() {
    this.loading = false;
  }

  private loadPowerShellVersions() {
    const api = new PowerShellVersionsApi();
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
