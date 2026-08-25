import { css, nothing, PropertyValues } from 'lit';
import '../components/dorc-spinner';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/vaadin-lumo-styles/icons.js';
import '../icons/iron-icons.js';
import '@vaadin/confirm-dialog';
import '@vaadin/text-field';
import '@vaadin/dialog';
import '../components/add-daemon';
import '../components/edit-daemon';
import '@vaadin/grid/vaadin-grid-column';
import { columnBodyRenderer } from '@vaadin/grid/lit';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { navigate } from '../router/router';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import {
  DaemonApiModel,
  RefDataDaemonsApi,
  ServerDaemonsApi
} from '../apis/dorc-api';
import type { ServerApiModel } from '../apis/dorc-api';
import GlobalCache from '../global-cache';
import '@vaadin/tooltip';
import { ref } from 'lit/directives/ref.js';
import { UnsavedChangesGuard } from '../components/unsaved-changes-guard';

@customElement('page-daemons-list')
export class PageDaemonsList extends ResponsiveMixin(PageElement) {
  private readonly unsavedChanges = new UnsavedChangesGuard();

  @property({ type: Array }) daemons: Array<DaemonApiModel> = [];

  @property({ type: Array }) filteredDaemons: Array<DaemonApiModel> = [];

  @property({ type: Boolean }) details = false;

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  @state() private editingDaemon: DaemonApiModel | null = null;

  @state() addDaemonDialogOpened = false;

  @state() editDaemonDialogOpened = false;

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
      vaadin-dialog::part(overlay) {
        top: 16px;
        overflow: auto;
        max-width: calc(100vw - 32px);
      }
      .row-actions vaadin-button {
        padding: 0;
        margin: 0 2px;
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

      <vaadin-dialog
        ${ref(this.unsavedChanges.attach)}
        id="add-daemon-dialog"
        header-title="Add Daemon"
        draggable
        width="560px"
        .opened="${this.addDaemonDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.addDaemonDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderAddDaemon, [])}
        ${dialogFooterRenderer(this.renderAddDaemonFooter, [])}
      ></vaadin-dialog>

      <vaadin-dialog
        ${ref(this.unsavedChanges.attach)}
        id="edit-daemon-dialog"
        header-title="Edit Daemon"
        draggable
        width="560px"
        .opened="${this.editDaemonDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.editDaemonDialogOpened = e.detail.value;
        }}"
        ${dialogRenderer(this.renderEditDaemon, [this.editingDaemon])}
        ${dialogFooterRenderer(this.renderEditDaemonFooter, [])}
      ></vaadin-dialog>

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
        ${
          this.pendingDelete
            ? html`<div style="overflow-wrap: anywhere">
                Delete daemon
                <strong>${this.pendingDelete.Name}</strong>? This cannot be
                undone.
                ${
                  this.pendingDeleteAttachedServers.length > 0
                    ? html`<br /><br />Currently attached to
                        ${this.pendingDeleteAttachedServers.length}
                        server${this.pendingDeleteAttachedServers.length === 1 ? '' : 's'}:
                        <ul style="margin: 4px 0 0 0">
                          ${this.pendingDeleteAttachedServers.map(
                            name => html`<li>${name}</li>`
                          )}
                        </ul>
                        Deleting will detach the daemon from all of them.`
                    : html`<br /><br />No server mappings to remove.`
                }
              </div>`
            : html``
        }
      </vaadin-confirm-dialog>

      ${
        this.loading
          ? html` <dorc-spinner></dorc-spinner> `
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
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  path="AccountName"
                  header="Account Name"
                  resizable
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  path="ServiceType"
                  header="Type"
                  resizable
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  path="LastSeenDate"
                  header="Last Seen"
                  resizable
                  direction="desc"
                  ?hidden="${this._narrowScreen}"
                  ${columnBodyRenderer(this._lastSeenRenderer, [])}
                ></vaadin-grid-sort-column>
                <vaadin-grid-column
                  header="Actions"
                  width="180px"
                  flex-grow="0"
                  ${columnBodyRenderer(this._rowActionsRenderer, [
                    this.isAdmin,
                    this.isPowerUser
                  ])}
                ></vaadin-grid-column>
              </vaadin-grid>
            `
      } `;
  }

  private _lastSeenRenderer = (daemon: DaemonApiModel) => {
    const raw = daemon.LastSeenDate;
    if (!raw) {
      return html`<span style="color: var(--dorc-text-secondary, #888)"
        >Never</span
      >`;
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

    return html`<span title="${tooltip}" style="color: ${color}"
      >${relative}</span
    >`;
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

  private _rowActionsRenderer = (daemon: DaemonApiModel) =>
    html`<div class="row-actions">
      <vaadin-button
        aria-label="View audit history"
        theme="icon"
        @click="${() => this.openAudit(daemon)}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="View audit history"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="vaadin:calendar-user"
          style="color: var(--dorc-link-color)"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        aria-label="Edit daemon"
        theme="icon"
        ?hidden="${!(this.isAdmin || this.isPowerUser)}"
        @click="${() => this.openEdit(daemon)}"
      >
        <vaadin-tooltip slot="tooltip" text="Edit daemon"></vaadin-tooltip>
        <vaadin-icon
          icon="lumo:edit"
          style="color: var(--dorc-link-color)"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        aria-label="Delete daemon"
        theme="icon"
        ?hidden="${!this.isAdmin}"
        @click="${() => this.requestDelete(daemon)}"
      >
        <vaadin-tooltip slot="tooltip" text="Delete daemon"></vaadin-tooltip>
        <vaadin-icon
          icon="icons:delete"
          style="color: var(--dorc-error-color)"
        ></vaadin-icon>
      </vaadin-button>
    </div>`;

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

  private renderAddDaemon = () =>
    html`<add-daemon id="add-daemon"></add-daemon>`;

  private renderAddDaemonFooter = () => html`
    <vaadin-button @click="${() => (this.addDaemonDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  /** Gated on `editingDaemon` so each edit gets a freshly-built form. */
  private renderEditDaemon = () =>
    this.editingDaemon
      ? html`<edit-daemon
          id="edit-daemon"
          .daemon="${this.editingDaemon}"
        ></edit-daemon>`
      : nothing;

  private renderEditDaemonFooter = () => html`
    <vaadin-button @click="${() => (this.editDaemonDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  daemonCreated() {
    this.getDaemonsList();
    this.addDaemonDialogOpened = false;
  }

  daemonUpdated() {
    this.getDaemonsList();
    this.editDaemonDialogOpened = false;
    this.editingDaemon = null;
  }

  openEdit(daemon: DaemonApiModel) {
    this.editingDaemon = { ...daemon };
    this.editDaemonDialogOpened = true;
  }

  openAudit(daemon: DaemonApiModel) {
    const id = daemon.Id ?? 0;
    if (id <= 0) return;
    void navigate(`/daemons/audit?daemonId=${id}`);
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
    this.addDaemonDialogOpened = true;
  }
}
