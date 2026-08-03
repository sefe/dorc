import { live } from 'lit/directives/live.js';
import { columnBodyRenderer } from '@vaadin/grid/lit';
import { css, PropertyValues } from 'lit';
import '../components/dorc-spinner';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/dialog';
import '@vaadin/text-field';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import { ConfigValueApiModel, RefDataConfigApi } from "../apis/dorc-api";
import { Checkbox } from '@vaadin/checkbox';
import '../components/grid-button-groups/config-value-controls';
import '../components/add-config-value';
import { RefDataRolesApi } from '../apis/dorc-api';

@customElement('page-config-values-list')
export class PageConfigValuesList extends ResponsiveMixin(PageElement) {
  @state() addConfigValueDialogOpened = false;

  @property({ type: Array }) configValues: Array<ConfigValueApiModel> = [];

  @property({ type: Array }) filteredConfigValues: Array<ConfigValueApiModel> = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property({ type: Boolean }) isAdmin = false;

  private loading = true;

  constructor() {
    super();
    this.getConfigValuesList();
  }

  private getConfigValuesList() {
    const api = new RefDataConfigApi();
    api.refDataConfigGet().subscribe({
      next: (data: ConfigValueApiModel[]) => {
        this.setConfigValues(data);
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading config values')
    });
  }

  // `isAdmin` gates the two checkbox columns and is in their dependency
  // arrays, so setting it is enough — the manual grid.requestContentUpdate()
  // this used to need is what the directive does.
  private loadRoles(): void {
    const api = new RefDataRolesApi();
    api.refDataRolesGet().subscribe({
      next: (roles: string[]) => {
        this.isAdmin = roles.find(p => p === 'Admin') !== undefined;
      },
      error: err => {
        console.error('Failed to load roles', err);
        this.isAdmin = false;
      }
    });
  }

  private updateConfigItem(updated: ConfigValueApiModel): void {
    const api = new RefDataConfigApi();
    const id = updated.Id;

    if (id == null) {
      console.error(`Missing Id on ConfigValueApiModel; keys: ${Object.keys(updated)}`);
      return;
    }

    api.refDataConfigPut({ id, configValueApiModel: updated }).subscribe({
      next: () => {
        this.getConfigValuesList();
      },
      error: (err: any) => {
        console.error('Update failed', err);
        this.getConfigValuesList();
      }
    });
  }

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;
      }
      vaadin-grid#grid {
        flex: 1;
        min-height: 0;
        --divider-color: var(--dorc-border-color);
        width: 100%;
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

      vaadin-dialog::part(overlay) {
        top: 16px;
        overflow: auto;
        max-width: calc(100vw - 32px);
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
      <div style="display: inline">
        <vaadin-text-field
          style="padding-left: 5px; width: 50%;"
          placeholder="Search"
          @value-changed="${this.updateSearch}"
          clear-button-visible
          helper-text="Use | for multiple search terms"
        >
          <vaadin-icon slot="prefix" icon="vaadin:search"></vaadin-icon>
        </vaadin-text-field>
        <vaadin-button
          title="Add Config Value"
          style="width: 250px"
          @click="${this.addConfigValue}"
        >
          <vaadin-icon
            icon="vaadin:options"
            style="color: var(--dorc-link-color)"
          ></vaadin-icon>
          Add Config Value...
        </vaadin-button>
      </div>
      <vaadin-dialog
        id="add-config-value-dialog"
        header-title="Add Config Value"
        draggable
        width="560px"
        .opened="${this.addConfigValueDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.addConfigValueDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderAddConfigValue, [])}
        ${dialogFooterRenderer(this.renderAddConfigValueFooter, [])}
      ></vaadin-dialog>
      ${this.loading
        ? html`
            <dorc-spinner></dorc-spinner>
          `
        : html`
            <vaadin-grid
              id="grid"
              .items=${this.filteredConfigValues}
              column-reordering-allowed
              multi-sort
              theme="compact row-stripes no-row-borders no-border"
            >
              <vaadin-grid-sort-column
                path="Key"
                header="Config Name"
                resizable
                width="300px"
                flex-grow="0"
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="Secure"
                header="Is Secure"
                resizable
                width="100px"
                flex-grow="0"
                ${columnBodyRenderer(this.isSecuredRenderer, [this.isAdmin])}
                ?hidden="${this._narrowScreen}"
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="IsForProd"
                header="Is For Prod"
                resizable
                width="100px"
                flex-grow="0"
                ${columnBodyRenderer(this.isForProdRenderer, [this.isAdmin])}
                ?hidden="${this._narrowScreen}"
              ></vaadin-grid-sort-column>
              <vaadin-grid-column
                header="Config Value"
                ${columnBodyRenderer(this.variableValueControlsRenderer, [this.isAdmin])}
                resizable
                flex-grow="1"
              ></vaadin-grid-column>
            </vaadin-grid>
          `}
    `;
  }

  firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);
    this.loadRoles();

    this.addEventListener(
      'config-value-created',
      this.configValueCreated as EventListener
    );

    this.addEventListener(
      'config-value-deleted',
      this.getConfigValuesList as EventListener
    );
  }

  private renderAddConfigValue = () =>
    html`<add-config-value></add-config-value>`;

  private renderAddConfigValueFooter = () => html`
    <vaadin-button @click="${() => (this.addConfigValueDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  configValueCreated() {
    this.getConfigValuesList();
    this.addConfigValueDialogOpened = false;
  }

  variableValueControlsRenderer(configValue: ConfigValueApiModel) {
    return html`<config-value-controls
      .value="${configValue}"
    ></config-value-controls>`;
  }

  isSecuredRenderer(configValue: ConfigValueApiModel) {
    return html`<vaadin-checkbox
      ?disabled="${!this.isAdmin}"
      .checked="${live(configValue.Secure as boolean)}"
      @change="${(e: Event) =>
        this.updateConfigItem({
          ...configValue,
          Secure: (e.target as Checkbox).checked
        })}"
    ></vaadin-checkbox>`;
  }

  isForProdRenderer(configValue: ConfigValueApiModel) {
    return html`<vaadin-checkbox
      ?disabled="${!this.isAdmin}"
      .checked="${live(configValue.IsForProd as boolean)}"
      @change="${(e: Event) =>
        this.updateConfigItem({
          ...configValue,
          IsForProd: (e.target as Checkbox).checked
        })}"
    ></vaadin-checkbox>`;
  }

  updateSearch(e: CustomEvent) {
    const value = (e.detail.value as string) || '';
    const filters = value
      .trim()
      .split('|')
      .map(filter => new RegExp(filter, 'i'));

    this.filteredConfigValues = this.configValues.filter(({ Key, Value }) =>
      filters.some(filter => filter.test(Key || '') || filter.test(Value || ''))
    );
  }

  setConfigValues(configValueApiModels: ConfigValueApiModel[]) {
    this.configValues = configValueApiModels;
    this.filteredConfigValues = this.configValues;
    this.loading = false;
  }

  addConfigValue() {
    this.addConfigValueDialogOpened = true;
  }
}