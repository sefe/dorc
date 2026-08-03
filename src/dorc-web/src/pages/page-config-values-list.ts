import { css, PropertyValues, render } from 'lit';
import '../components/dorc-spinner';
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
import { RefDataRolesApi } from '../apis/dorc-api';
import { NarrowListController } from '../helpers/narrow-list-controller';
import { listRowStyles, narrowListRenderers } from '../components/dorc-list-row';
import { configValuesNarrow } from '../row-templates/batch-row-templates';

@customElement('page-config-values-list')
export class PageConfigValuesList extends PageElement {
  /** Narrow-mode (HLPS §3.4): container-driven list rendering. */
  narrowList = new NarrowListController(this as unknown as HTMLElement & import('lit').ReactiveControllerHost);

  private _nl = narrowListRenderers(
    () => configValuesNarrow(this).template,
    () => configValuesNarrow(this).bar ?? {}
  );

  @property({ type: Array }) configValues: Array<ConfigValueApiModel> = [];

  @property({ type: Array }) filteredConfigValues: Array<ConfigValueApiModel> = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property({ type: Boolean }) isAdmin = false;

  private loading = true;

  constructor() {
    super();
    this.getConfigValuesList();
    this.isSecuredRenderer = this.isSecuredRenderer.bind(this);
    this.isForProdRenderer = this.isForProdRenderer.bind(this);
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

  private loadRoles(): void {
    const api = new RefDataRolesApi();
    api.refDataRolesGet().subscribe({
      next: (roles: string[]) => {
        this.isAdmin = roles.find(p => p === 'Admin') !== undefined;
        const grid = this.shadowRoot?.getElementById('grid') as any;
        grid?.requestContentUpdate?.();
        this.requestUpdate();
      },
      error: err => {
        console.error('Failed to load roles', err);
        this.isAdmin = false;
        const grid = this.shadowRoot?.getElementById('grid') as any;
        grid?.requestContentUpdate?.();
        this.requestUpdate();
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
    return [
      listRowStyles,
      css`
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

      paper-dialog.size-position {
        top: 16px;
        overflow: auto;
        padding: 10px;
        width: 560px;
        max-width: calc(100vw - 32px);
        box-sizing: border-box;
      }
      @media (max-width: 768px) {
        vaadin-grid-cell-content {
          white-space: normal;
          word-wrap: break-word;
          overflow-wrap: break-word;
        }
      }
    `
    ];
  }

  render() {
    return html`
      <div class="dorc-toolbar">
        <vaadin-text-field
          placeholder="Search"
          @value-changed="${this.updateSearch}"
          clear-button-visible
          helper-text="Use | for multiple search terms"
        >
          <vaadin-icon slot="prefix" icon="vaadin:search"></vaadin-icon>
        </vaadin-text-field>
        <vaadin-button
          title="Add Config Value"
          @click="${this.addConfigValue}"
        >
          <vaadin-icon
            icon="vaadin:options"
            style="color: var(--dorc-link-color)"
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
              <vaadin-grid-column
          flex-grow="1"
          ?hidden="${!this.narrowList.narrow}"
          .headerRenderer="${this._nl.bar}"
          .renderer="${this._nl.row}"
        ></vaadin-grid-column>
        <vaadin-grid-sort-column ?hidden="${this.narrowList.narrow}"
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
                .renderer=${this.isSecuredRenderer}
                ?hidden="${this.narrowList.narrow}"
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="IsForProd"
                header="Is For Prod"
                resizable
                width="100px"
                flex-grow="0"
                .renderer=${this.isForProdRenderer}
                ?hidden="${this.narrowList.narrow}"
              ></vaadin-grid-sort-column>
              <vaadin-grid-column ?hidden="${this.narrowList.narrow}"
                header="Config Value"
                .renderer=${this.variableValueControlsRenderer}
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
    checkbox.disabled = !this.isAdmin;

    checkbox.addEventListener('change', async () => {
      await this.updateConfigItem({...configValueApiModel, Secure: checkbox.checked
      });
    });

    render(checkbox, root);
  }

  isForProdRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<ConfigValueApiModel>
  ) {
    const configValueApiModel = model.item as ConfigValueApiModel;

    const checkbox = new Checkbox();

    checkbox.checked = configValueApiModel.IsForProd as boolean;
    checkbox.disabled = !this.isAdmin;

    checkbox.addEventListener('change', async () => {
      await this.updateConfigItem({...configValueApiModel, IsForProd: checkbox.checked
      });
    });

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