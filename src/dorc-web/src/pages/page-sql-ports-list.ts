import { css, PropertyValues } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '../components/add-sql-port';
import '@polymer/paper-dialog';
import { PaperDialogElement } from '@polymer/paper-dialog';
import '@vaadin/text-field';
import { customElement, property } from 'lit/decorators.js';
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
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 110px);
        --divider-color: rgb(223, 232, 239);
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
      paper-dialog.size-position {
        top: 16px;
        overflow: auto;
        padding: 10px;
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
            style="color: cornflowerblue"
          ></vaadin-icon
          >Add SQL Port...
        </vaadin-button>
      </div>
      <paper-dialog
        class="size-position"
        id="add-sqlport-dialog"
        allow-click-through
        modal
      >
        <add-sql-port id="add-sql-port"></add-sql-port>
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
  
  sqlPortCreated() {
    this.getSqlPortsList();
  
    const dialog = this.shadowRoot?.getElementById(
      'add-sqlport-dialog'
    ) as PaperDialogElement;
    dialog.close();
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
    const dialog = this.shadowRoot?.getElementById('add-sqlport-dialog') as PaperDialogElement;
    dialog.open();
  }
}
