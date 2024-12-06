import '@vaadin/button';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-column';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import { Checkbox } from '@vaadin/checkbox';
import { css, render } from 'lit';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/add-edit-environment';
import '../components/grid-button-groups/env-controls';
import { EnvironmentApiModel, RefDataRolesApi } from '../apis/dorc-api';
import { RefDataEnvironmentsApi } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { AddEditAccessControl } from '../components/add-edit-access-control';
import '../components/add-edit-access-control';
import '../components/hegs-dialog';
import { HegsDialog } from '../components/hegs-dialog';
import { AddEditEnvironment } from '../components/add-edit-environment';

@customElement('page-environments-list')
export class PageEnvironmentsList extends PageElement {
  @property({ type: Array }) environments: EnvironmentApiModel[] = [];

  @property({ type: Array })
  filteredEnvironments: EnvironmentApiModel[] = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property({ type: String }) project = '';

  @property({ type: String }) size = '';

  @property({ type: Boolean }) private loading = true;

  @property({ type: String }) private secureName = '';

  @property({ type: Object }) private newEnvironment:
    | EnvironmentApiModel
    | undefined;

  public userRoles!: string[];

  @property({ type: Boolean }) public userRolesLoaded = false;

  @query('#dialog') dialog!: HegsDialog;

  @query('#add-environment') addEditEnvironment!: AddEditEnvironment;

  static get styles() {
    return css`
      :host {
        position: relative;
        overflow-y: hidden; /* Hide vertical scrollbar */
      }
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 110px);
        --divider-color: rgb(223, 232, 239);
        margin-top: 60px;
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
    `;
  }

  render() {
    return html`
      <div style="position: fixed; height: 60px; width: 100%; display: inline">
        <vaadin-horizontal-layout>
          <vaadin-text-field
            style="padding-left: 5px; min-width: 50%; padding-right: 5px"
            placeholder="Search"
            @value-changed="${this.updateSearch}"
            clear-button-visible
            helper-text="Use | for multiple search terms"
          >
            <vaadin-icon slot="prefix" icon="vaadin:search"></vaadin-icon>
          </vaadin-text-field>
          <vaadin-button
            title="Add Environment"
            style="width: 250px; padding-left: 10px"
            @click="${this.addEnvironment}"
          >
            <vaadin-icon
              icon="vaadin:cube"
              style="color: cornflowerblue"
            ></vaadin-icon>
            Add Environment...
          </vaadin-button>
        </vaadin-horizontal-layout>
      </div>

      <hegs-dialog id="dialog" title="Create Environment">
        <add-edit-environment
          id="add-environment"
          .addMode="${true}"
          .readonly="${false}"
          @environment-added="${this.closeAddEnv}"
          .environment="${this.newEnvironment}"
        ></add-edit-environment>
      </hegs-dialog>

      <add-edit-access-control
        id="add-edit-access-control"
        .secureName="${this.secureName}"
      ></add-edit-access-control>
      <div>
        ${this.loading || !this.userRolesLoaded
          ? html`
              <div class="overlay" style="z-index: 2">
                <div class="overlay__inner">
                  <div class="overlay__content">
                    <span class="spinner"></span>
                  </div>
                </div>
              </div>
            `
          : html`
              <vaadin-grid
                id="grid"
                .items="${this.filteredEnvironments}"
                multi-sort
                theme="compact row-stripes no-row-borders no-border"
              >
                <vaadin-grid-sort-column
                  resizable
                  path="EnvironmentName"
                  header="Name"
                  style="color:lightgray"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  resizable
                  path="Details.EnvironmentOwner"
                  header="Owner"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  resizable
                  path="Details.Description"
                  header="Description"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  resizable
                  path="EnvironmentSecure"
                  header="Secure"
                  .renderer="${this._envSecureRenderer}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  resizable
                  path="EnvironmentIsProd"
                  header="Prod"
                  .renderer="${this._envIsProdRenderer}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  resizable
                  path="Details.FileShare"
                  header="File Share"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  resizable
                  path="Details.Notes"
                  header="Notes"
                ></vaadin-grid-sort-column>
                <vaadin-grid-column
                  .renderer="${this._envDetailsButtonsRenderer}"
                ></vaadin-grid-column>
              </vaadin-grid>
            `}
      </div>
    `;
  }

  async firstUpdated() {
    this.getEnvs();

    this.addEventListener(
      'open-access-control',
      this.openAccessControl as EventListener
    );
  }

  constructor() {
    super();

    const refDataRolesApi = new RefDataRolesApi();
    refDataRolesApi.refDataRolesGet().subscribe({
      next: (data: string[]) => {
        this.userRoles = data;
      },
      error: (err: string) => console.error(err),
      complete: () => {
        this.userRolesLoaded = true;
        console.log('finished loading user roles');
      }
    });
  }

  openAccessControl(e: CustomEvent) {
    this.secureName = e.detail.Name as string;
    const type = e.detail.Type as number;

    const addEditAccessControl = this.shadowRoot?.getElementById(
      'add-edit-access-control'
    ) as AddEditAccessControl;

    addEditAccessControl.open(this.secureName, type);
  }

  _envSecureRenderer(
    root: HTMLElement,
    _column: GridColumn,
    { item }: GridItemModel<EnvironmentApiModel>
  ) {
    const envDetails = item as EnvironmentApiModel;
    const checkbox = new Checkbox();

    checkbox.checked = envDetails.EnvironmentSecure ?? false;
    checkbox.disabled = true;

    render(checkbox, root);
  }

  _envIsProdRenderer(
    root: HTMLElement,
    _column: GridColumn,
    { item }: GridItemModel<EnvironmentApiModel>
  ) {
    const envDetails = item as EnvironmentApiModel;
    const checkbox = new Checkbox();

    checkbox.checked = envDetails.EnvironmentIsProd ?? false;
    checkbox.disabled = true;

    render(checkbox, root);
  }

  _envDetailsButtonsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    { item }: GridItemModel<EnvironmentApiModel>
  ) {
    const envDetails = item as EnvironmentApiModel;
    render(
      html` <env-controls .envDetails="${envDetails}"></env-controls>`,
      root
    );
  }

  closeAddEnv(e: CustomEvent) {
    const env = e.detail.environment as EnvironmentApiModel;

    const model: EnvironmentApiModel[] = JSON.parse(
      JSON.stringify(this.environments)
    );
    model.push(env);

    this.setEnvironments(model);

    this.dialog.close();
  }

  setEnvironments(environmentDetailsApiModels: EnvironmentApiModel[]) {
    const sortedEnvs = environmentDetailsApiModels.sort(this.sortEnvs);
    this.environments = sortedEnvs;
    this.filteredEnvironments = sortedEnvs;
    this.loading = false;
  }

  sortEnvs(a: EnvironmentApiModel, b: EnvironmentApiModel): number {
    if (String(a.EnvironmentName) > String(b.EnvironmentName)) return 1;
    return -1;
  }

  updateSearch(e: CustomEvent) {
    const value = (e.detail.value as string) || '';
    const filters = value
      .trim()
      .split('|')
      .map(filter => new RegExp(filter, 'i'));

    this.filteredEnvironments = this.environments.filter(
      ({ EnvironmentName, Details }) =>
        filters.some(
          filter =>
            filter.test(EnvironmentName || '') ||
            filter.test(Details?.EnvironmentOwner || '') ||
            filter.test(Details?.Description || '')
        )
    );
  }

  addEnvironment() {
    this.addEditEnvironment.clearAllFields();
    this.dialog.open = true;
  }

  private getEnvs() {
    if (this.environments === undefined || this.environments.length === 0) {
      this.loading = true;

      const api = new RefDataEnvironmentsApi();
      api.refDataEnvironmentsGet({ env: '' }).subscribe(
        (data: EnvironmentApiModel[]) => {
          this.setEnvironments(data);
        },

        (err: any) => console.error(err),
        () => console.log('done loading environments')
      );
    }
  }
}
