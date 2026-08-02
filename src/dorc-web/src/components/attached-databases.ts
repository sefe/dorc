import '@vaadin/dialog';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { ref } from 'lit/directives/ref.js';
import '@vaadin/button';
import '@vaadin/grid';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, LitElement, nothing, render } from 'lit';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/edit-database-permissions';
import './grid-button-groups/database-env-controls.ts';
import '../components/view-database-permissions';
import {
  DatabaseApiModel,
} from '../apis/dorc-api';
import { EditDatabasePermissions } from './edit-database-permissions';
import { ViewDatabasePermissions } from './view-database-permissions';
import { map } from 'lit/directives/map.js';

@customElement('attached-databases')
export class AttachedDatabases extends ResponsiveMixin(LitElement) {
  @state() private permissionsDialogOpened = false;

  @state() private viewPermissionsDialogOpened = false;

  @state() private editDbId: number | null = null;

  @state() private viewDbId: number | null = null;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) private readonly = true;

  @property({ type: Array })
  public databases: Array<DatabaseApiModel> | undefined = [];

  static get styles() {
    return css`
      .center {
        margin: 10px 20px 10px;
        width: 50%;
        padding: 10px;
      }

      .inline {
        display: inline-block;
        vertical-align: middle;
      }

      vaadin-dialog::part(overlay) {
        top: 16px;
        overflow: auto;
        max-width: calc(100vw - 32px);
      }

      vaadin-grid#grid {
        overflow: hidden;
        width: calc(100% - 4px);
        --divider-color: var(--dorc-border-color);
      }

      .tag {
        font-size: var(--lumo-font-size-s);
        font-family: monospace;
        background-color: var(--dorc-chip-bg);
        color: var(--dorc-chip-text);
        display: inline-block;
        padding: 3px;
        margin: 3px;
        text-decoration: none;
        border-radius: 3px;
      }

      .tag:hover {
        background-color: var(--dorc-badge-bg);
        color: var(--dorc-badge-text);
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
      <vaadin-grid
        id="grid"
        .items=${this.databases}
        theme="compact row-stripes no-row-borders no-border"
        all-rows-visible
      >
        <vaadin-grid-column
          path="ServerName"
          header="Instance"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Name"
          header="Database"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this.applicationTagsRenderer}"
          resizable
          header="Application Tag"
          ?hidden="${this._narrowScreen}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="ArrayName"
          header="Array Name"
          resizable
          ?hidden="${this._narrowScreen}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this._boundDatabasesButtonsRenderer}"
          resizable
        >
        </vaadin-grid-column>
      </vaadin-grid>

      <vaadin-dialog
        id="permissions"
        header-title="Manage Database Permissions"
        draggable
        .opened="${this.permissionsDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.permissionsDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderEditPermissions, [this.editDbId, this.envId])}
        ${dialogFooterRenderer(this.renderEditPermissionsFooter, [])}
      ></vaadin-dialog>

      <vaadin-dialog
        id="viewPermissions"
        header-title="Database Permissions"
        draggable
        .opened="${this.viewPermissionsDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.viewPermissionsDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderViewPermissions, [
          this.viewDbId,
          this.envId,
          this.readonly
        ])}
        ${dialogFooterRenderer(this.renderViewPermissionsFooter, [])}
      ></vaadin-dialog>
    `;
  }

  private applicationTagsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: GridItemModel<DatabaseApiModel>
  ) => {
    const database = model.item;
    const appTags =
      database.Type !== undefined &&
      database.Type !== null &&
      database.Type.length > 0
        ? database.Type?.split(';')
        : [];

    render(
      html`
        ${map(
          appTags,
          value =>
            html` <button
              style="border: 0px"
              class="tag"
              @click="${() =>
                this.dispatchEvent(
                  new CustomEvent('filter-tags-database-list', {
                    detail: {
                      value
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

  _boundDatabasesButtonsRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DatabaseApiModel>
  ) => {
    const db = model.item as DatabaseApiModel;

    render(
      html` <database-env-controls
        .dbDetails="${db}"
        .envId="${this.envId}"
        .readonly="${this.readonly}"
        @database-detached="${() =>
          this.dispatchEvent(
            new CustomEvent('database-detached', { detail: { db } })
          )
        }"
        @manage-database-perms="${() => {
          this.editDbId = db.Id || 0;
          this.permissionsDialogOpened = true;
        }}"
        @view-database-perms="${() => {
          this.viewDbId = db.Id || 0;
          this.viewPermissionsDialogOpened = true;
        }}"
      ></database-env-controls>`,
      root
    );
  }

  /**
   * These two components are configured through imperative methods, and the
   * elements do not exist until the dialog opens. `ref` fires exactly when the
   * element is created, which removes the old configure-then-open ordering
   * rather than trying to re-time it.
   */
  private renderEditPermissions = () =>
    this.editDbId !== null
      ? html`<edit-database-permissions
          id="edit"
          .envId="${this.envId}"
          ${ref(el => {
            if (!el || this.editDbId === null) return;
            const edit = el as EditDatabasePermissions;
            edit.reset();
            edit.setDbId(this.editDbId);
          })}
        ></edit-database-permissions>`
      : nothing;

  private renderEditPermissionsFooter = () => html`
    <vaadin-button @click="${() => (this.permissionsDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  private renderViewPermissions = () =>
    this.viewDbId !== null
      ? html`<view-database-permissions
          id="view"
          .envId="${this.envId}"
          .readonly="${this.readonly}"
          ${ref(el => {
            if (!el || this.viewDbId === null) return;
            const view = el as ViewDatabasePermissions;
            view.setDbId(this.viewDbId);
            view.loadDatabaseUsers();
          })}
        ></view-database-permissions>`
      : nothing;

  private renderViewPermissionsFooter = () => html`
    <vaadin-button @click="${() => (this.viewPermissionsDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;
}
