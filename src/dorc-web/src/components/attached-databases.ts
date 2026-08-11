import '@vaadin/dialog';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { ref } from 'lit/directives/ref.js';
import '@vaadin/button';
import '@vaadin/grid';
import { columnBodyRenderer } from '@vaadin/grid/lit';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, LitElement, nothing } from 'lit';
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
          ${columnBodyRenderer(this.applicationTagsRenderer, [])}
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
          ${columnBodyRenderer(this.databaseControlsRenderer, [
            this.envId,
            this.readonly
          ])}
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
          // Clearing the id destroys the content, so the next open recreates it
          // and re-seeds through `ref`. Without this the element persists and
          // the stable callback would not fire for the next row.
          if (!this.permissionsDialogOpened) this.editDbId = null;
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
          if (!this.viewPermissionsDialogOpened) this.viewDbId = null;
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

  private applicationTagsRenderer = (database: DatabaseApiModel) => {
    const appTags =
      database.Type !== undefined &&
      database.Type !== null &&
      database.Type.length > 0
        ? database.Type?.split(';')
        : [];

    return html`
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
    `;
  };

  private databaseControlsRenderer = (db: DatabaseApiModel) => html`
    <database-env-controls
      .dbDetails="${db}"
      .envId="${this.envId}"
      .readonly="${this.readonly}"
      @database-detached="${() =>
        this.dispatchEvent(
          new CustomEvent('database-detached', { detail: { db } })
        )}"
      @manage-database-perms="${() => {
        this.editDbId = db.Id || 0;
        this.permissionsDialogOpened = true;
      }}"
      @view-database-perms="${() => {
        this.viewDbId = db.Id || 0;
        this.viewPermissionsDialogOpened = true;
      }}"
    ></database-env-controls>
  `;

  /**
   * These two components are configured through imperative methods, and the
   * elements do not exist until the dialog opens. `ref` fires exactly when the
   * element is created, which removes the old configure-then-open ordering
   * rather than trying to re-time it.
   */
  /**
   * Seeds the permission dialogs, once per open.
   *
   * These are class fields, not arrows written inline in the renderer. Lit's
   * `ref` compares callback identity, so a fresh arrow each render re-fires the
   * directive on every renderer invocation — and the overlay invokes the
   * renderer itself on open and again on header/footer changes, which meant
   * four loads per open and four `reset()`s, the first carrying the previous
   * row's id.
   *
   * Seeding is driven by the element's lifetime instead: `viewDbId`/`editDbId`
   * are cleared on close, so the element is destroyed and recreated per open
   * and the callback runs exactly once with the right row.
   */
  private seedViewPermissions = (el?: Element) => {
    if (!el || this.viewDbId === null) return;
    const view = el as ViewDatabasePermissions;
    view.setDbId(this.viewDbId);
    view.loadDatabaseUsers();
  };

  private seedEditPermissions = (el?: Element) => {
    if (!el || this.editDbId === null) return;
    const edit = el as EditDatabasePermissions;
    edit.reset();
    edit.setDbId(this.editDbId);
  };

  private renderEditPermissions = () =>
    this.editDbId !== null
      ? html`<edit-database-permissions
          id="edit"
          .envId="${this.envId}"
          ${ref(this.seedEditPermissions)}
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
          ${ref(this.seedViewPermissions)}
        ></view-database-permissions>`
      : nothing;

  private renderViewPermissionsFooter = () => html`
    <vaadin-button @click="${() => (this.viewPermissionsDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;
}
