import { keyed } from 'lit/directives/keyed.js';
import '@vaadin/checkbox';
import { columnBodyRenderer } from '@vaadin/grid/lit';
import '@vaadin/button';
import '../components/dorc-spinner';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-column';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/text-field';
import '@vaadin/dialog';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogRenderer } from '@vaadin/dialog/lit';
import { css } from 'lit';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/add-edit-environment';
import '../components/clone-environment';
import '../components/grid-button-groups/env-controls';
import {
  AccessControlType,
  EnvironmentApiModel,
  RefDataRolesApi
} from '../apis/dorc-api';
import { RefDataEnvironmentsApi } from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import { AddEditAccessControl } from '../components/add-edit-access-control';
import '../components/add-edit-access-control';
import { CloneEnvironment } from '../components/clone-environment';
import { ref } from 'lit/directives/ref.js';
import { UnsavedChangesGuard } from '../components/unsaved-changes-guard';

@customElement('page-environments-list')
export class PageEnvironmentsList extends ResponsiveMixin(PageElement) {
  private readonly unsavedChanges = new UnsavedChangesGuard();

  @property({ type: Array }) environments: EnvironmentApiModel[] = [];

  @property({ type: Array })
  filteredEnvironments: EnvironmentApiModel[] = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property({ type: String }) project = '';

  @property({ type: String }) size = '';

  @property({ type: Boolean }) loading = true;

  @property({ type: String }) secureName = '';

  @property({ type: Object }) newEnvironment: EnvironmentApiModel | undefined;

  public userRoles!: string[];

  @property({ type: Boolean }) public userRolesLoaded = false;

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  @state() private addEnvDialogOpened = false;

  /** Bumped per open so each one gets a freshly built form. */
  @state() private addEnvSeq = 0;

  @query('#clone-environment') cloneEnvironmentComponent!: CloneEnvironment;

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;
      }
      vaadin-grid#grid {
        overflow: hidden;
        flex: 1;
        min-height: 0;
        --divider-color: var(--dorc-border-color);
        margin-top: 60px;
      }
      vaadin-grid#grid::part(prod-not-secure) {
        background-color: var(--dorc-warning-bg);
        color: var(--dorc-warning-text);
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
              style="color: var(--dorc-link-color)"
            ></vaadin-icon>
            Add Environment...
          </vaadin-button>
        </vaadin-horizontal-layout>
      </div>

      <vaadin-dialog
        ${ref(this.unsavedChanges.attach)}
        id="dialog"
        header-title="Create Environment"
        draggable
        .opened="${this.addEnvDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.addEnvDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderAddEnvironment, [
          this.newEnvironment,
          this.environments,
          this.addEnvSeq
        ])}
      ></vaadin-dialog>

      <clone-environment
        id="clone-environment"
        @environment-cloned="${this.closeCloneEnv}"
      ></clone-environment>

      <add-edit-access-control
        id="add-edit-access-control"
        .secureName="${this.secureName}"
      ></add-edit-access-control>
      <div
        style="flex: 1; min-height: 0; display: flex; flex-direction: column;"
      >
        ${
          this.loading || !this.userRolesLoaded
            ? html` <dorc-spinner></dorc-spinner> `
            : html`
                <vaadin-grid
                  id="grid"
                  .items="${this.filteredEnvironments}"
                  multi-sort
                  theme="compact row-stripes no-row-borders no-border"
                  .cellPartNameGenerator="${this._cellPartNameGenerator}"
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
                    ?hidden="${this._narrowScreen}"
                  ></vaadin-grid-sort-column>
                  <vaadin-grid-sort-column
                    resizable
                    path="Details.Description"
                    header="Description"
                    ?hidden="${this._narrowScreen}"
                  ></vaadin-grid-sort-column>
                  <vaadin-grid-sort-column
                    resizable
                    path="EnvironmentSecure"
                    header="Secure"
                    ${columnBodyRenderer(this._envSecureRenderer, [])}
                    ?hidden="${this._narrowScreen}"
                  ></vaadin-grid-sort-column>
                  <vaadin-grid-sort-column
                    resizable
                    path="EnvironmentIsProd"
                    header="Prod"
                    ${columnBodyRenderer(this._envIsProdRenderer, [])}
                    ?hidden="${this._narrowScreen}"
                  ></vaadin-grid-sort-column>
                  <vaadin-grid-sort-column
                    resizable
                    path="Details.FileShare"
                    header="File Share"
                    ?hidden="${this._narrowScreen}"
                  ></vaadin-grid-sort-column>
                  <vaadin-grid-sort-column
                    resizable
                    path="Details.Notes"
                    header="Notes"
                    ?hidden="${this._narrowScreen}"
                  ></vaadin-grid-sort-column>
                  <vaadin-grid-column
                    ${columnBodyRenderer(this._envDetailsButtonsRenderer, [
                      this.isAdmin,
                      this.isPowerUser
                    ])}
                  ></vaadin-grid-column>
                </vaadin-grid>
              `
        }
      </div>
    `;
  }

  async firstUpdated() {
    this.getEnvs();

    this.addEventListener(
      'open-access-control',
      this.openAccessControl as EventListener
    );

    this.addEventListener(
      'clone-environment',
      this.openCloneDialog as EventListener
    );
  }

  constructor() {
    super();

    const refDataRolesApi = new RefDataRolesApi();
    refDataRolesApi.refDataRolesGet().subscribe({
      next: (data: string[]) => {
        this.userRoles = data;
        this.isAdmin = this.userRoles.find(p => p === 'Admin') !== undefined;
        this.isPowerUser =
          this.userRoles.find(p => p === 'PowerUser') !== undefined;
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
    const type = e.detail.Type as AccessControlType;

    const addEditAccessControl = this.shadowRoot?.getElementById(
      'add-edit-access-control'
    ) as AddEditAccessControl;

    addEditAccessControl.open(this.secureName, type);
  }

  openCloneDialog(e: CustomEvent) {
    const environment = e.detail.Environment as EnvironmentApiModel;
    this.cloneEnvironmentComponent.open(environment);
  }

  closeCloneEnv(e: CustomEvent) {
    const env = e.detail.environment as EnvironmentApiModel;

    const model: EnvironmentApiModel[] = JSON.parse(
      JSON.stringify(this.environments)
    );
    model.push(env);

    this.setEnvironments(model);
  }

  _cellPartNameGenerator = (
    _column: GridColumn,
    { item }: GridItemModel<EnvironmentApiModel>
  ): string => {
    const env = item as EnvironmentApiModel;
    if (env.EnvironmentIsProd && !env.EnvironmentSecure) {
      return 'prod-not-secure';
    }
    return '';
  };

  _envSecureRenderer(envDetails: EnvironmentApiModel) {
    const checkbox = html`<vaadin-checkbox
      disabled
      .checked="${envDetails.EnvironmentSecure ?? false}"
    ></vaadin-checkbox>`;

    if (envDetails.EnvironmentIsProd && !envDetails.EnvironmentSecure) {
      return html`<div style="display:flex;align-items:center;gap:4px">
        ${checkbox}
        <vaadin-icon
          icon="vaadin:warning"
          title="Production environment without Secure flag"
          style="color:var(--dorc-warning-text);width:var(--lumo-icon-size-s);height:var(--lumo-icon-size-s)"
        ></vaadin-icon>
      </div>`;
    }
    return checkbox;
  }

  _envIsProdRenderer(envDetails: EnvironmentApiModel) {
    return html`<vaadin-checkbox
      disabled
      .checked="${envDetails.EnvironmentIsProd ?? false}"
    ></vaadin-checkbox>`;
  }

  _envDetailsButtonsRenderer = (item: EnvironmentApiModel) => {
    const envDetails = item as EnvironmentApiModel;
    return html` <env-controls
      .envDetails="${envDetails}"
      .isAdmin="${this.isAdmin}"
      .isPowerUser="${this.isPowerUser}"
    ></env-controls>`;
  };

  private renderAddEnvironment = () => html`
    ${keyed(
      this.addEnvSeq,
      html`
        <add-edit-environment
          id="add-environment"
          .addMode="${true}"
          .readonly="${false}"
          @environment-added="${this.closeAddEnv}"
          .environment="${this.newEnvironment}"
        ></add-edit-environment>
      `
    )}
  `;

  closeAddEnv(e: CustomEvent) {
    const env = e.detail.environment as EnvironmentApiModel;

    const model: EnvironmentApiModel[] = JSON.parse(
      JSON.stringify(this.environments)
    );
    model.push(env);

    this.setEnvironments(model);

    this.addEnvDialogOpened = false;
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
    // The form lives inside the dialog renderer, so it does not exist until
    // the dialog has opened at least once. Reaching for it through @query and
    // calling clearAllFields() threw, which ran *before* the line that opens
    // the dialog — so the button did nothing at all. Bumping the key rebuilds
    // the element instead, and its own connectedCallback resets it in addMode.
    this.addEnvSeq += 1;
    this.addEnvDialogOpened = true;
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
