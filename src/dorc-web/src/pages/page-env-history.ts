import { Grid, GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid-sorter';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/text-field';
import { TextField } from '@vaadin/text-field';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { css, PropertyValues, render } from 'lit';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import AppConfig from '../app-config';
import '../components/grid-button-groups/edit-comments-controls';
import { Configuration, EnvironmentHistoryApiModel } from '../apis/dorc-api';
import { RefDataEnvironmentsHistoryApi } from '../apis/dorc-api/apis';
import { PageElement, PageLocation } from '../helpers/page-element';
import { NarrowListController } from '../helpers/narrow-list-controller';
import {
  listRowStyles,
  renderListBar,
  renderListRow
} from '../components/dorc-list-row';
import { environmentHistoryRowTemplate } from '../row-templates/environment-history-row-template';
import { router } from '../router/router';
import { EnvironmentHistoryApiModelExtended } from '../components/model-extensions/environment-history-api-model-extended';

@customElement('page-env-history')
export class PageEnvironmentHistory extends PageElement {
  @property({ type: Array })
  envHistory: EnvironmentHistoryApiModelExtended[] = [];

  @query('#grid') grid: Grid | undefined;

  /** Narrow-mode (HLPS §3.4): container-driven; replaces ResponsiveMixin. */
  list = new NarrowListController(this);

  private rowTemplate = environmentHistoryRowTemplate();

  private _openedNarrowItems: EnvironmentHistoryApiModelExtended[] = [];

  constructor() {
    super();
    this.location = router.location as PageLocation;

    const appConfig = new Configuration({
      basePath: new AppConfig().dorcApi
    });
    const api = new RefDataEnvironmentsHistoryApi(appConfig);

    const envId = parseInt(new URLSearchParams(location.search).get('id')!, 10);

    api.refDataEnvironmentsHistoryGet({ id: envId }).subscribe({
      next: (data: EnvironmentHistoryApiModel[]) => {
        this.setEnvHistory(data);
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading env history')
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
        --divider-color: var(--dorc-border-color);
      }
      vaadin-grid#grid {
        flex: 1;
        min-height: 0;
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
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
    `
    ];
  }

  render() {
    return html`
      <vaadin-grid
        id="grid"
        .items=${this.envHistory}
        column-reordering-allowed
        multi-sort
        theme="compact row-stripes no-row-borders no-border"
        .rowDetailsRenderer=${this.list.narrow
          ? this._listDetailsRenderer
          : undefined}
        .detailsOpenedItems=${this._openedNarrowItems}
        @active-item-changed=${this._onNarrowActiveItem}
      >
        <vaadin-grid-column
          flex-grow="1"
          ?hidden="${!this.list.narrow}"
          .headerRenderer="${this._listBarRenderer}"
          .renderer="${this._listRowRenderer}"
        ></vaadin-grid-column>
        <vaadin-grid-sort-column
          resizable
          path="EnvName"
          header="Environment Name"
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          resizable
          path="UpdatedDate"
          .renderer="${this._dateRenderer}"
          header="Updated Date"
          width="170px"
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          resizable
          path="UpdatedBy"
          header="Updated By"
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          resizable
          path="UpdateType"
          header="Update Type"
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          resizable
          path="FromValue"
          header="Old Version"
          width="170px"
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          resizable
          path="ToValue"
          header="New Version"
          width="170px"
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-sort-column>
        <vaadin-grid-sort-column
          resizable
          path="Details"
          header="Details"
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-sort-column>
        <vaadin-grid-column
          resizable
          header="Comment"
          .renderer="${this._commentRenderer}"
          .attachedPageEnvironmentHistory="${this}"
          width="270px"
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this._editButtonsRenderer}"
          width="14em"
          ?hidden="${this.list.narrow}"
        ></vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  // ---- Narrow-mode list rendering (HLPS §3.4) ----

  private _listRowRenderer = (
    root: HTMLElement,
    _: GridColumn,
    model: GridItemModel<EnvironmentHistoryApiModelExtended>
  ) => {
    render(renderListRow(this.rowTemplate, model.item), root);
  };

  private _listDetailsRenderer = (
    root: HTMLElement,
    _: GridColumn,
    model: GridItemModel<EnvironmentHistoryApiModelExtended>
  ) => {
    render(this.rowTemplate.details!(model.item), root);
  };

  private _onNarrowActiveItem = (e: CustomEvent) => {
    if (!this.list.narrow) return;
    const item = e.detail.value as EnvironmentHistoryApiModelExtended | null;
    if (!item) return;
    const idx = this._openedNarrowItems.indexOf(item);
    this._openedNarrowItems =
      idx === -1
        ? [...this._openedNarrowItems, item]
        : [
            ...this._openedNarrowItems.slice(0, idx),
            ...this._openedNarrowItems.slice(idx + 1)
          ];
    (e.currentTarget as { activeItem?: unknown }).activeItem = null;
    this.requestUpdate();
  };

  /**
   * Sort-only bar (the view has no filters): the declarative sort surface
   * that lives in the hidden header row is re-exposed here, sorters staying
   * connected per DECISION-bar-mechanism.md.
   */
  private _listBarRenderer = (root: HTMLElement) => {
    render(
      renderListBar({
        sort: html`<vaadin-grid-sorter path="UpdatedDate" direction="desc"
            >Date</vaadin-grid-sorter
          >
          <vaadin-grid-sorter path="EnvName">Environment</vaadin-grid-sorter>
          <vaadin-grid-sorter path="UpdatedBy">User</vaadin-grid-sorter>`
      }),
      root
    );
  };

  setEnvHistory(envHistory: EnvironmentHistoryApiModel[]) {
    this.envHistory = envHistory as EnvironmentHistoryApiModelExtended[];
    this.envHistory.forEach(element => this.getDate(element));
    this.envHistory = this.envHistory.sort(this.sortEnvs);
  }

  sortEnvs(
    a: EnvironmentHistoryApiModelExtended,
    b: EnvironmentHistoryApiModelExtended
  ): number {
    return (
      new Date(b.UpdatedDate || new Date()).getTime() -
      new Date(a.UpdatedDate || new Date()).getTime()
    );
  }

  getDate(element: EnvironmentHistoryApiModelExtended): void {
    const splitDT: string[] = element.UpdateDate?.split(' ') as string[];
    const splitDate = splitDT[0].split('/');
    const splitTime = splitDT[1].split(':');

    element.UpdatedDate = new Date(
      parseInt(splitDate[2], 10),
      parseInt(splitDate[1], 10) - 1,
      parseInt(splitDate[0], 10),
      parseInt(splitTime[0], 10),
      parseInt(splitTime[1], 10),
      parseInt(splitTime[2], 10)
    );
  }

  _dateRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<EnvironmentHistoryApiModelExtended>
  ) {
    const history = model.item as EnvironmentHistoryApiModelExtended;
    const time = history.UpdatedDate?.toLocaleTimeString('en-GB');
    const date = history.UpdatedDate?.toLocaleDateString('en-GB', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric'
    });
    render(html`<div>${`${date} ${time}`}</div>`, root);
  }

  _commentRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<EnvironmentHistoryApiModel>
  ) {
    const history = model.item as EnvironmentHistoryApiModel;
    render(
      html`<vaadin-text-field
        style="width: 270px"
        id="${`comments${model.index}`}"
        readonly
        focus-target
        .value="${history.Comment ?? ''}"
        .history="${history}"
        @value-changed="${(e: CustomEvent) => {
          const textField = e.detail as TextField;
          history.Comment = textField.value;
        }}"
      >
      </vaadin-text-field>`,
      root
    );
  }

  _editButtonsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<EnvironmentHistoryApiModelExtended>
  ) {
    render(
      html`
        <edit-comments-controls .model="${model}"> </edit-comments-controls>
      `,
      root
    );
  }

  _editClick(e: CustomEvent) {
    if (this.grid !== undefined) {
      const model = e.detail
        .Model as GridItemModel<EnvironmentHistoryApiModelExtended>;
      this.updateTextFieldsVisibility(model.index, true);
      const textField = this.grid.querySelector(
        `#comments${model.index}`
      ) as unknown as TextField;
      textField.focus();
    }
  }

  _cancelClick(event: CustomEvent) {
    if (this.grid !== undefined) {
      const model = event.detail
        .Model as GridItemModel<EnvironmentHistoryApiModelExtended>;
      this.updateTextFieldsVisibility(model.index, false);

      this.grid.clearCache();
    }
  }

  updateTextFieldsVisibility(index: number, editing: boolean) {
    if (this.grid !== undefined) {
      const textField = this.grid.querySelector(
        `#comments${index}`
      ) as unknown as TextField;
      textField.readonly = !editing;
    }
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'env-history-comments-edit',
      this._editClick as EventListener
    );
    this.addEventListener(
      'env-history-comments-cancel',
      this._cancelClick as EventListener
    );
  }
}
