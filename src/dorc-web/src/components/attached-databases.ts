import { normaliseTags } from '../helpers/tag-parser';
import '@vaadin/dialog';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import '@vaadin/button';
import '@vaadin/grid';
import { columnBodyRenderer } from '@vaadin/grid/lit';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, LitElement, nothing } from 'lit';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/database-tags';
import './grid-button-groups/database-env-controls.ts';
import { DatabaseApiModel } from '../apis/dorc-api';
import type { DatabaseAccessView } from './database-access-panel';
import { map } from 'lit/directives/map.js';
import { renderDatabaseType } from '../helpers/database-type';

@customElement('attached-databases')
export class AttachedDatabases extends ResponsiveMixin(LitElement) {
  @state() private tagsDialogOpened = false;

  @state() private selectedDatabase: DatabaseApiModel | undefined;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) readonly = true;

  @property({ type: Boolean }) canManageDatabaseAccess = false;

  @property({ type: Array })
  public databases: Array<DatabaseApiModel> | undefined = [];

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        flex: 1;
        min-height: 0;
        height: 100%;
      }

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
        flex: 1;
        min-height: 0;
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
      >
        <vaadin-grid-column
          path="ServerName"
          header="Instance"
          width="220px"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Name"
          header="Database"
          width="220px"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          ${columnBodyRenderer(this.databaseTypeRenderer, [])}
          header="Type"
          width="64px"
          flex-grow="0"
          text-align="center"
        ></vaadin-grid-column>
        <vaadin-grid-column
          ${columnBodyRenderer(this.tagsRenderer, [])}
          resizable
          width="200px"
          header="Tags"
          ?hidden="${this._narrowScreen}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="ArrayName"
          header="Array Name"
          width="140px"
          resizable
          ?hidden="${this._narrowScreen}"
        ></vaadin-grid-column>
        <vaadin-grid-column
          width="${this.canManageDatabaseAccess ? '220px' : '120px'}"
          flex-grow="0"
          ${columnBodyRenderer(this.databaseControlsRenderer, [
            this.envId,
            this.readonly,
            this.canManageDatabaseAccess
          ])}
          resizable
        >
        </vaadin-grid-column>
      </vaadin-grid>

      <vaadin-dialog
        id="database-tags-dialog"
        header-title="Edit Database Tags for ${this.selectedDatabase?.Name ?? ''}"
        draggable
        .opened="${this.tagsDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.tagsDialogOpened = e.detail.value;
          if (!this.tagsDialogOpened) this.selectedDatabase = undefined;
        }}"
        ${dialogRenderer(this.renderDatabaseTags, [this.selectedDatabase])}
        ${dialogFooterRenderer(this.renderDatabaseTagsFooter, [])}
      ></vaadin-dialog>
    `;
  }

  private tagsRenderer = (database: DatabaseApiModel) => {
    const appTags = normaliseTags(database.Tags);

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

  private databaseTypeRenderer = renderDatabaseType;

  private databaseControlsRenderer = (db: DatabaseApiModel) => html`
    <database-env-controls
      .dbDetails="${db}"
      .envId="${this.envId}"
      .readonly="${this.readonly}"
      .canManageDatabaseAccess="${this.canManageDatabaseAccess}"
      @database-detached="${() =>
        this.dispatchEvent(
          new CustomEvent('database-detached', { detail: { db } })
        )}"
      @manage-database-tags="${() => {
        this.selectedDatabase = db;
        this.tagsDialogOpened = true;
      }}"
      @manage-database-access="${(
        event: CustomEvent<{ view: DatabaseAccessView }>
      ) =>
        this.dispatchEvent(
          new CustomEvent('manage-database-access', {
            detail: { database: db, view: event.detail.view }
          })
        )}"
    ></database-env-controls>
  `;

  private renderDatabaseTags = () =>
    this.selectedDatabase
      ? html`<database-tags
          .database="${this.selectedDatabase}"
          @database-tags-updated="${this.databaseTagsUpdated}"
        ></database-tags>`
      : nothing;

  private renderDatabaseTagsFooter = () => html`
    <vaadin-button @click="${() => (this.tagsDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  private databaseTagsUpdated() {
    this.tagsDialogOpened = false;
    this.dispatchEvent(
      new CustomEvent('database-tags-updated', {
        bubbles: true,
        composed: true
      })
    );
  }
}