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
  GridDataProviderCallback,
  GridDataProviderParams,
  GridFilterDefinition,
  GridItemModel,
  GridSorterDefinition
} from '@vaadin/grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Checkbox } from '@vaadin/checkbox';
import { PageElement } from '../helpers/page-element';
import {
  PagedDataSorting,
  RefDataScriptsApi,
  ScriptApiModel
} from '../apis/dorc-api';
import { GetScriptsListResponseDto, PagedDataFilter } from '../apis/dorc-api';
import GlobalCache from '../global-cache';
import '../components/hegs-json-viewer';
import { HegsJsonViewer } from '../components/hegs-json-viewer';

@customElement('page-scripts-list')
export class PageScriptsList extends PageElement {
  @property({ type: Array }) scripts: Array<ScriptApiModel> = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property() nameFilterValue = '';

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  public userRoles!: string[];

  @property({ type: Boolean }) private loading = true;

  @property({ type: Boolean }) private rolesLoading = true;

  static get styles() {
    return css`
      :host {
        display: flex;
        width: 100%;
        overflow: hidden;
      }
      vaadin-grid#grid {
        --divider-color: rgb(223, 232, 239);
        height: calc(100vh - 56px);
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
    `;
  }

  render() {
    return html`
      <div
        class="overlay"
        style="z-index: 2"
        ?hidden="${!(this.loading || this.rolesLoading)}"
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
            .dataProvider="${this.getScripts}"
            style="z-index: 1"
          >
            <vaadin-grid-column
              header="Enabled"
              resizable
              width="80px"
              flex-grow="0"
              .renderer="${this.enabledRenderer}"
              .userRoles="${this.userRoles}"
            >
            </vaadin-grid-column>
            <vaadin-grid-column
              path="Name"
              header="Script Name"
              resizable
              .headerRenderer="${this.nameHeaderRenderer}"
              width="500px"
              flex-grow="0"
            >
            </vaadin-grid-column>
            <vaadin-grid-sort-column
              path="NonProdOnly"
              header="Non Prod Only"
              resizable
              width="120px"
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
          </vaadin-grid>`}
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'scripts-loaded',
      this.scriptsLoaded as EventListener
    );
  }

  constructor() {
    super();

    GlobalCache.getInstance().allRolesResp?.subscribe((data: Array<string>) => {
      this.userRoles = data;

      if (this.userRoles.find(p => p === 'Admin') === undefined) {
        this.isAdmin = false;
      } else {
        this.isAdmin = true;
      }

      if (this.userRoles.find(p => p === 'PowerUser') === undefined) {
        this.isPowerUser = false;
      } else {
        this.isPowerUser = true;
      }

      this.rolesLoading = false;
    });
  }

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
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const roles = _column.userRoles;

    let isAdmin: boolean;
    if (roles && roles.find((p: string) => p === 'Admin') !== undefined) {
      isAdmin = true;
    } else {
      isAdmin = false;
    }

    let isPowerUser: boolean;
    if (roles && roles.find((p: string) => p === 'PowerUser') !== undefined) {
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

  nameHeaderRenderer(root: HTMLElement) {
    root.innerHTML = `
        <vaadin-grid-sorter path='Name'>Name</vaadin-grid-sorter>
        <vaadin-grid-filter path="Name">
          <vaadin-text-field
            clear-button-visible
            slot="filter" focus-target style='width: 100%' 
                           theme='small'
          ></vaadin-text-field>
        </vaadin-grid-filter>
      `;
    const filter: any = root.querySelector('vaadin-grid-filter');
    root
      .querySelector('vaadin-text-field')!
      .addEventListener('value-changed', (e: any) => {
        filter.value = e.detail.value;
      });
  }

  pathHeaderRenderer(root: HTMLElement) {
    root.innerHTML = `
        <vaadin-grid-sorter path='Path'>Path</vaadin-grid-sorter>
        <vaadin-grid-filter path="Path">
          <vaadin-text-field
            clear-button-visible
            slot="filter" focus-target style='width: 100%' 
                           theme='small'
          ></vaadin-text-field>
        </vaadin-grid-filter>
      `;
    const filter: any = root.querySelector('vaadin-grid-filter');
    root
      .querySelector('vaadin-text-field')!
      .addEventListener('value-changed', (e: any) => {
        filter.value = e.detail.value;
      });
  }

  private scriptsLoaded() {
    this.loading = false;
  }

  getScripts(
    params: GridDataProviderParams<ScriptApiModel>,
    callback: GridDataProviderCallback<ScriptApiModel>
  ) {
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
  }
}
