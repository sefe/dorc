import { css, PropertyValues, render } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@polymer/paper-dialog';
import '@vaadin/text-field';
import { PaperDialogElement } from '@polymer/paper-dialog';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { ConfigValueApiModel, RefDataConfigApi } from "../apis/dorc-api";
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { GridItemModel } from '@vaadin/grid';
import { Checkbox } from '@vaadin/checkbox';
import '../components/grid-button-groups/config-value-controls';
import '../components/add-config-value';

@customElement('page-config-values-list')
export class PageConfigValuesList extends PageElement {
  @property({ type: Array }) configValues: Array<ConfigValueApiModel> = [];
  
  @property({ type: Array }) filteredConfigValues: Array<ConfigValueApiModel> = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

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

  static get styles() {
    return css`
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 110px);
        --divider-color: rgb(223, 232, 239);
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

      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
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
      <div style="display: inline">
        <vaadin-text-field
          style="padding-left: 5px; width: 50%;"
          placeholder="Search"
          @value-changed="${this.updateSearch}"
          clear-button-visible
          helper-text="Use | for multiple search terms"
        >
          <vaaadin-icon slot="prefix" icon="vaadin:search"></vaaadin-icon>
        </vaadin-text-field>
        <vaadin-button
          title="Add Config Value"
          style="width: 250px"
          @click="${this.addConfigValue}"
        >
          <vaadin-icon
            icon="vaadin:options"
            style="color: cornflowerblue"
          ></vaadin-icon>
          Add Config Value...
        </vaadin-button>
      </div>
      <paper-dialog
        class="size-position"
        id="add-config-value-dialog"
        allow-click-through
        modal
      >
        <add-config-value></add-config-value>
        <div style="display: flex; justify-content: flex-end">
          <vaadin-button dialog-confirm>Close</vaadin-button>
        </div>
      </paper-dialog>
      ${this.loading
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
              .items=${this.filteredConfigValues}
              column-reordering-allowed
              multi-sort
              theme="compact row-stripes no-row-borders no-border"
            >
              <vaadin-grid-sort-column
                path="Key"
                header="Config Name"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="AccountName"
                header="Is Secure"
                resizable
                .renderer="${this.isSecuredRenderer}"
              ></vaadin-grid-sort-column>
              <vaadin-grid-column
                header="Config Value"
                .renderer="${this.variableValueControlsRenderer}"
                resizable
                auto-width
              ></vaadin-grid-column>
            </vaadin-grid>
          `}
    `;
  }

  firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'config-value-created',
      this.configValueCreated as EventListener
    );

    this.addEventListener(
      'config-value-deleted',
      this.getConfigValuesList as EventListener
    );
  }

  configValueCreated() {
    this.getConfigValuesList();

    const dialog = this.shadowRoot?.getElementById(
      'add-config-value-dialog'
    ) as PaperDialogElement;
    dialog.close();
  }

  variableValueControlsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<ConfigValueApiModel>
  ) {
    render(
      html` <config-value-controls .value="${model.item}">
      </config-value-controls>`,
      root
    );
  }

  isSecuredRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<ConfigValueApiModel>
  ) {
    const configValueApiModel = model.item as ConfigValueApiModel;

    const checkbox = new Checkbox();

    checkbox.checked = configValueApiModel.Secure as boolean;
    checkbox.disabled = true;

    render(checkbox, root);
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
    const paperDialogElement = this.shadowRoot?.getElementById(
      'add-config-value-dialog'
    ) as PaperDialogElement;
    paperDialogElement.open();
  }
}
