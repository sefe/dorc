import { css, PropertyValues } from 'lit';
import '../components/dorc-spinner';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '../components/add-sql-port';
import '@vaadin/dialog';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import '@vaadin/text-field';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { SqlPortApiModel } from '../apis/dorc-api';
import { RefDataSqlPortsApi } from '../apis/dorc-api';
import GlobalCache from '../global-cache';

@customElement('page-sql-ports-list')
export class PageSqlPortsList extends PageElement {
  @property({ type: Array }) sqlPorts: Array<SqlPortApiModel> = [];

  @property({ type: Array }) filteredSqlPorts: Array<SqlPortApiModel> = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property({ type: Boolean }) private isAdmin = false;

  public userRoles!: string[];

  private loading = true;

  /**
   * Dialog visibility. Reactive rather than an imperative handle, so the
   * template is the single source of truth for whether the dialog is showing.
   */
  @state() addSqlPortDialogOpened = false;

  constructor() {
    super();
    this.getUserRoles();
    this.getSqlPortsList();
  }

  private getUserRoles() {
    const gc = GlobalCache.getInstance();
    if (gc.userRoles === undefined) {
      gc.allRolesResp?.subscribe({
        next: (userRoles: string[]) => {
          this.setUserRoles(userRoles);
        }
      });
    } else {
      this.setUserRoles(gc.userRoles);
    }
  }

  private setUserRoles(userRoles: string[]) {
    this.userRoles = userRoles;
    this.isAdmin = this.userRoles.find(p => p === 'Admin') !== undefined;
  }

  
  private getSqlPortsList() {
    const api = new RefDataSqlPortsApi();
    api.refDataSqlPortsGet().subscribe(
      (data: SqlPortApiModel[]) => {
        this.setSqlPorts(data);
      },

      (err: any) => console.error(err),
      () => console.log('done loading daemons')
    );
  }

  static get styles() {
    return css`
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
      /* Carries over the old paper-dialog.size-position rule. Reachable
         because the dialog's renderer root is appended to the <vaadin-dialog>
         element, which lives in this shadow root. */
      vaadin-dialog::part(overlay) {
        top: 16px;
        overflow: auto;
        max-width: calc(100vw - 32px);
      }
    `;
  }

  render() {
    return html`<div style="display: inline">
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
          title="Add SQL Port"
          style="width: 250px"
          .disabled="${!this.isAdmin}"
          @click="${this.addSqlPort}"
        >
          <vaadin-icon
            icon="vaadin:connect"
            style="color: var(--dorc-link-color)"
          ></vaadin-icon
          >Add SQL Port...
        </vaadin-button>
      </div>
      <vaadin-dialog
        id="add-sqlport-dialog"
        header-title="Add SQL Port"
        draggable
        .opened="${this.addSqlPortDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.addSqlPortDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderAddSqlPort, [])}
        ${dialogFooterRenderer(this.renderAddSqlPortFooter, [])}
      ></vaadin-dialog>
      ${this.loading
        ? html`
            <dorc-spinner></dorc-spinner>
          `
        : html`
            <vaadin-grid
              id="grid"
              .items=${this.filteredSqlPorts}
              column-reordering-allowed
              multi-sort
              theme="compact row-stripes no-row-borders no-border"
            >
              <vaadin-grid-sort-column
                path="InstanceName"
                header="Instance Name"
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="SqlPort"
                header="Port"
              ></vaadin-grid-sort-column>
            </vaadin-grid>
          `} `;
  }  

  firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);
  
    this.addEventListener(
      'sqlport-created',
      this.sqlPortCreated as EventListener
    );
  }
  
  private renderAddSqlPort = () =>
    html`<add-sql-port id="add-sql-port"></add-sql-port>`;

  /**
   * `dialog-confirm` was inert on `<vaadin-dialog>`, so the close path is
   * explicit. Escape and outside-click also close, via `opened-changed`.
   */
  private renderAddSqlPortFooter = () => html`
    <vaadin-button @click="${() => (this.addSqlPortDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  sqlPortCreated() {
    this.getSqlPortsList();
    this.addSqlPortDialogOpened = false;
  }

  updateSearch(e: CustomEvent) {
    const value = (e.detail.value as string) || '';
    const filters = value
      .trim()
      .split('|')
      .map(filter => new RegExp(filter.replace("\\","\\\\"), 'i'));

    this.filteredSqlPorts = this.sqlPorts.filter(({ InstanceName, SqlPort }) =>
      filters.some(
        filter => filter.test(InstanceName || '') || filter.test(SqlPort || '')
      )
    );
  }

  setSqlPorts(sqlPortAPIModels: SqlPortApiModel[]) {
    this.sqlPorts = sqlPortAPIModels;
    this.filteredSqlPorts = sqlPortAPIModels;
    this.loading = false;
  }

  addSqlPort() {
    this.addSqlPortDialogOpened = true;
  }
}
