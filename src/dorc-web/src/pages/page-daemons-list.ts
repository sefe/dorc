import { css, PropertyValues, render } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/vaadin-lumo-styles/icons.js';
import '../icons/iron-icons.js';
import '@vaadin/confirm-dialog';
import '@vaadin/text-field';
import '@polymer/paper-dialog';
import '../components/add-daemon';
import '../components/edit-daemon';
import type { GridItemModel } from '@vaadin/grid';
import type { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { PaperDialogElement } from '@polymer/paper-dialog';
import { Router } from '@vaadin/router';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { DaemonApiModel, RefDataDaemonsApi, ServerDaemonsApi } from '../apis/dorc-api';
import type { ServerApiModel } from '../apis/dorc-api';
import GlobalCache from '../global-cache';

@customElement('page-daemons-list')
export class PageDaemonsList extends PageElement {
  @property({ type: Array }) daemons: Array<DaemonApiModel> = [];

  @property({ type: Array }) filteredDaemons: Array<DaemonApiModel> = [];

  @property({ type: Boolean }) details = false;

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  @state() private editingDaemon: DaemonApiModel | null = null;

  @state() private confirmDeleteOpen = false;

  @state() private pendingDelete: DaemonApiModel | null = null;

  @state() private pendingDeleteAttachedServers: string[] = [];

  private loading = true;

  public userRoles!: string[];

  constructor() {
    super();
    this.getUserRoles();
    this.getDaemonsList();
  }

  private getUserRoles() {
    const gc = GlobalCache.getInstance();
    if (gc.userRoles === undefined) {
      gc.allRolesResp?.subscribe({
        next: (userRoles: string[]) => this.setUserRoles(userRoles)
      });
    } else {
      this.setUserRoles(gc.userRoles);
    }
  }

  private setUserRoles(userRoles: string[]) {
    this.userRoles = userRoles;
    this.isAdmin = userRoles.find(p => p === 'Admin') !== undefined;
    this.isPowerUser = userRoles.find(p => p === 'PowerUser') !== undefined;
  }

  private getDaemonsList() {
    const api = new RefDataDaemonsApi();
    api.refDataDaemonsGet().subscribe(
      (data: DaemonApiModel[]) => {
        this.setDaemons(data);
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
        --divider-color: var(--dorc-border-color);
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
        border-color: var(--dorc-border-color);
        border-top-color: var(--dorc-link-color);
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
      .row-actions vaadin-button {
        padding: 0;
        margin: 0 2px;
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
          title="Add Daemon"
          style="width: 250px"
          @click="${this.addDaemon}"
          ?hidden="${!(this.isAdmin || this.isPowerUser)}"
        >
          <vaadin-icon
            icon="vaadin:cog"
            style="color: var(--dorc-link-color)"
          ></vaadin-icon
          >Add Daemon...
        </vaadin-button>
      </div>

      <paper-dialog
        class="size-position"
        id="add-daemon-dialog"
        allow-click-through
        modal
      >
        <add-daemon id="add-daemon"></add-daemon>
        <div style="display: flex; justify-content: flex-end">
          <vaadin-button dialog-confirm>Close</vaadin-button>
        </div>
      </paper-dialog>

      <paper-dialog
        class="size-position"
        id="edit-daemon-dialog"
        allow-click-through
        modal
      >
        ${this.editingDaemon
          ? html`<edit-daemon
              id="edit-daemon"
              .daemon="${this.editingDaemon}"
            ></edit-daemon>`
          : html``}
        <div style="display: flex; justify-content: flex-end">
          <vaadin-button dialog-confirm>Close</vaadin-button>
        </div>
      </paper-dialog>

      <vaadin-confirm-dialog
        .opened="${this.confirmDeleteOpen}"
        @opened-changed="${(e: CustomEvent) => {
          this.confirmDeleteOpen = (e.detail as any).value;
        }}"
        header="Delete daemon"
        confirm-text="Delete"
        confirm-theme="error primary"
        cancel-button-visible
        @confirm="${this.performDelete}"
      >
        ${this.pendingDelete
          ? html`Delete daemon
              <strong>${this.pendingDelete.Name}</strong>? This cannot be
              undone.
              ${this.pendingDeleteAttachedServers.length > 0
                ? html`<br /><br />Currently attached to
                    ${this.pendingDeleteAttachedServers.length} server${this.pendingDeleteAttachedServers.length === 1 ? '' : 's'}:
                    <ul style="margin: 4px 0 0 0">
                      ${this.pendingDeleteAttachedServers.map(
                        name => html`<li>${name}</li>`
                      )}
                    </ul>
                    Deleting will detach the daemon from all of them.`
                : html`<br /><br />No server mappings to remove.`}`
          : html``}
      </vaadin-confirm-dialog>

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
              .items=${this.filteredDaemons}
              column-reordering-allowed
              multi-sort
              theme="compact row-stripes no-row-borders no-border"
            >
              <vaadin-grid-sort-column
                path="Name"
                header="Daemon Name"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="DisplayName"
                header="Display Name"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="AccountName"
                header="Account Name"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="ServiceType"
                header="Type"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="LastSeenDate"
                header="Last Seen"
                resizable
                direction="desc"
                .renderer="${this._lastSeenRenderer}"
              ></vaadin-grid-sort-column>
              <vaadin-grid-column
                header="Actions"
                width="180px"
                flex-grow="0"
                .renderer="${this._rowActionsRenderer}"
              ></vaadin-grid-column>
            </vaadin-grid>
          `} `;
  }

  private _lastSeenRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DaemonApiModel>
  ) => {
    const daemon = model.item;
    const raw = daemon.LastSeenDate;
    if (!raw) {
      render(
        html`<span style="color: var(--dorc-text-secondary, #888)">Never</span>`,
        root
      );
      return;
    }

    const dt = new Date(raw);
    const relative = this._formatRelativeTime(dt);
    const tooltip = `${dt.toLocaleString('en-GB')}${daemon.LastSeenStatus ? ' — ' + daemon.LastSeenStatus : ''}`;
    const status = daemon.LastSeenStatus?.toLowerCase();
    const color =
      status === 'running'
        ? 'var(--dorc-success-bg, inherit)'
        : status === 'stopped'
        ? 'inherit'
        : status == null || status === ''
        ? 'var(--dorc-error-color, inherit)'
        : 'inherit';

    render(
      html`<span title="${tooltip}" style="color: ${color}">${relative}</span>`,
      root
    );
  };

  private _formatRelativeTime(date: Date): string {
    const now = Date.now();
    const diffMs = now - date.getTime();
    const diffSec = Math.floor(diffMs / 1000);
    if (diffSec < 60) return 'just now';
    const diffMin = Math.floor(diffSec / 60);
    if (diffMin < 60) return `${diffMin} min ago`;
    const diffHr = Math.floor(diffMin / 60);
    if (diffHr < 24) return `${diffHr} hr ago`;
    const diffDay = Math.floor(diffHr / 24);
    if (diffDay < 30) return `${diffDay} day${diffDay === 1 ? '' : 's'} ago`;
    const diffMonth = Math.floor(diffDay / 30);
    if (diffMonth < 12) return `${diffMonth} mo ago`;
    const diffYear = Math.floor(diffDay / 365);
    return `${diffYear} yr${diffYear === 1 ? '' : 's'} ago`;
  }

  private _rowActionsRenderer = (
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DaemonApiModel>
  ) => {
    const daemon = model.item;
    render(
      html`<div class="row-actions">
        <vaadin-button
          title="View audit history"
          theme="icon"
          @click="${() => this.openAudit(daemon)}"
        >
          <vaadin-icon
            icon="vaadin:calendar-user"
            style="color: var(--dorc-link-color)"
          ></vaadin-icon>
        </vaadin-button>
        <vaadin-button
          title="Edit daemon"
          theme="icon"
          ?hidden="${!(this.isAdmin || this.isPowerUser)}"
          @click="${() => this.openEdit(daemon)}"
        >
          <vaadin-icon
            icon="lumo:edit"
            style="color: var(--dorc-link-color)"
          ></vaadin-icon>
        </vaadin-button>
        <vaadin-button
          title="Delete daemon"
          theme="icon"
          ?hidden="${!this.isAdmin}"
          @click="${() => this.requestDelete(daemon)}"
        >
          <vaadin-icon
            icon="icons:delete"
            style="color: var(--dorc-error-color)"
          ></vaadin-icon>
        </vaadin-button>
      </div>`,
      root
    );
  };

  firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'daemon-created',
      this.daemonCreated as EventListener
    );
    this.addEventListener(
      'daemon-updated',
      this.daemonUpdated as EventListener
    );
  }

  daemonCreated() {
    this.getDaemonsList();
    const dialog = this.shadowRoot?.getElementById(
      'add-daemon-dialog'
    ) as PaperDialogElement;
    dialog.close();
  }

  daemonUpdated() {
    this.getDaemonsList();
    const dialog = this.shadowRoot?.getElementById(
      'edit-daemon-dialog'
    ) as PaperDialogElement;
    dialog?.close();
    this.editingDaemon = null;
  }

  openEdit(daemon: DaemonApiModel) {
    this.editingDaemon = { ...daemon };
    const dialog = this.shadowRoot?.getElementById(
      'edit-daemon-dialog'
    ) as PaperDialogElement;
    dialog?.open();
  }

  openAudit(daemon: DaemonApiModel) {
    const id = daemon.Id ?? 0;
    if (id <= 0) return;
    Router.go(`/daemons/audit?daemonId=${id}`);
  }

  requestDelete(daemon: DaemonApiModel) {
    this.pendingDelete = daemon;
    this.pendingDeleteAttachedServers = [];

    if (daemon.Id && daemon.Id > 0) {
      const api = new ServerDaemonsApi();
      api.serverDaemonsByDaemonDaemonIdGet({ daemonId: daemon.Id }).subscribe({
        next: (servers: ServerApiModel[]) => {
          this.pendingDeleteAttachedServers = servers
            .map(s => s.Name ?? '')
            .filter(n => n.length > 0);
        },
        error: () => {
          // Swallow: open the dialog without the list; user still sees the generic warning.
        }
      });
    }

    this.confirmDeleteOpen = true;
  }

  performDelete() {
    const daemon = this.pendingDelete;
    if (!daemon || !daemon.Id) {
      this.confirmDeleteOpen = false;
      return;
    }
    const api = new RefDataDaemonsApi();
    api.refDataDaemonsDelete({ id: daemon.Id }).subscribe(
      () => {
        this.pendingDelete = null;
        this.confirmDeleteOpen = false;
        this.getDaemonsList();
      },
      (err: any) => {
        console.error('Failed to delete daemon', err);
        this.confirmDeleteOpen = false;
      }
    );
  }

  updateSearch(e: CustomEvent) {
    const value = (e.detail.value as string) || '';
    const filters = value
      .trim()
      .split('|')
      .map(filter => new RegExp(filter, 'i'));

    this.filteredDaemons = this.daemons.filter(({ DisplayName, Name }) =>
      filters.some(
        filter => filter.test(DisplayName || '') || filter.test(Name || '')
      )
    );
  }

  setDaemons(daemons: DaemonApiModel[]) {
    this.daemons = daemons;
    this.filteredDaemons = daemons;
    this.loading = false;
  }

  addDaemon() {
    const dialog = this.shadowRoot?.getElementById(
      'add-daemon-dialog'
    ) as PaperDialogElement;
    dialog.open();
  }
}
