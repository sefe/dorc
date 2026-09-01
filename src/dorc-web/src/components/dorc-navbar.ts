import '@vaadin/icons/vaadin-icons';
import '@vaadin/icons/vaadin-iconset.js';
import '@vaadin/icon';
import '@vaadin/tabs';
import { Tabs } from '@vaadin/tabs';
import { Tab } from '@vaadin/tabs/vaadin-tab';
import '@vaadin/vertical-layout';
import { css, html, LitElement, PropertyValues } from 'lit';
import { repeat } from 'lit/directives/repeat.js';
import { customElement, property, state } from 'lit/decorators.js';
import {
  DeploymentRequestApiModel,
  EnvironmentApiModel,
  MetadataApi,
  ProjectApiModel
} from '../apis/dorc-api';
import { urlForName } from '../router/router';
import './tabs/env-detail-tab';
import { EnvDetailTab } from './tabs/env-detail-tab';
import './tabs/project-envs-tab';
import { ProjectEnvsTab } from './tabs/project-envs-tab';
import './tabs/monitor-result-tab';
import { MonitorResultTab } from './tabs/monitor-result-tab';
import GlobalCache from '../global-cache.ts';
import {
  drawerShortcuts,
  toEnvShortcut,
  toProjectShortcut,
  toResultShortcut,
  envKey,
  projectKey,
  resultKey,
  type DrawerShortcutState,
  type EnvShortcut,
  type ProjectShortcut,
  type ResultShortcut
} from './drawer-shortcuts.ts';
import { EnvPageTabNames } from '../pages/page-environment.ts';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

@customElement('dorc-navbar')
export class DorcNavbar extends LitElement {
  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        font-family: var(--lumo-font-family);
      }

      main,
      main > * {
        display: flex;
        flex: 1;
        flex-direction: column;
      }

      main:empty ~ footer {
        display: none;
      }

      footer {
        padding: 1rem;
        text-align: center;
        background-color: var(--dorc-bg-tertiary);
      }

      vaadin-icon {
        padding-right: 0.2em;
        width: var(--lumo-icon-size-s);
        height: var(--lumo-icon-size-s);
      }

      a {
        color: inherit; /* blue colors for links too */
        text-decoration: inherit; /* no underline */
        padding-top: 2px;
        padding-bottom: 2px;
      }

      a.plain {
        text-decoration: underline;
        color: var(--dorc-link-color);
      }

      vaadin-tab {
        padding-top: 0px;
        padding-bottom: 0px;
      }

      /* Audit sub-items: was margin-left: 20px; width: 210px, which clipped below
         ~246px of drawer width and sat 4px off the SQL sub-items' indent (D-35). */
      .sub-item {
        display: block;
        padding-inline-start: var(--lumo-space-l);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      /* ── Shortcut tabs ──
         The three shortcut components render into light DOM (D-03), so they land
         inside this shadow root and these rules style them. One scoped stylesheet
         beats inline styles on every element.

         Replaces the previous fixed width: 270px + margin-left: 20px block,
         which overflowed its own tab at the default 300px drawer width and put the
         tail of every name underneath the close control (D-09). The old
         float: right on that control was dead code — float computes to none
         under position: absolute (D-38). */
      .shortcut-link {
        display: flex;
        align-items: center;
        gap: 0.25em;
        flex: 1;
        min-width: 0;
        padding-inline-start: var(--lumo-space-l);
        color: inherit;
        text-decoration: inherit;
      }

      .shortcut-link--stacked {
        flex-direction: column;
        align-items: flex-start;
        gap: 0;
      }

      .shortcut-line {
        display: flex;
        align-items: center;
        gap: 0.25em;
        min-width: 0;
        max-width: 100%;
      }

      /* min-width:0 is what actually allows a flex item to shrink below its
         content width, without which ellipsis never engages. */
      .shortcut-label,
      .shortcut-sublabel {
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .shortcut-sublabel {
        max-width: 100%;
        font-size: var(--lumo-font-size-s);
        /* Small text needs 4.5:1 (1.4.3); the plain token does not reach it. */
        color: var(--dorc-text-secondary-strong);
      }

      .shortcut-icon {
        flex-shrink: 0;
      }

      .shortcut-close {
        flex-shrink: 0;
        margin: 0;
        padding: 0;
        cursor: pointer;
        /* --dorc-link-color is 2.97:1 on white — just under 1.4.11's 3:1 for UI
           components — so the close control gets its own token. */
        color: var(--dorc-icon-interactive);
      }

      /* vaadin-tab is the flex container for a shortcut's link + close control. */
      vaadin-tab:has(.shortcut-close) {
        display: flex;
        align-items: center;
        gap: 0.25em;
      }
    `;
  }

  /** Rendered from the store, not mutated directly. */
  @state() private shortcuts: DrawerShortcutState = {
    environments: [],
    projects: [],
    results: []
  };

  private unsubscribe: (() => void) | undefined;

  public userRoles!: string[];

  @property() metaData = '';

  @property({ type: Boolean }) isAdmin = false;

  @state() private auditMenuExpanded = false;

  render() {
    return html`
      <vaadin-iconset name="inline" size="24">
        <svg>
          <defs>
            <g id="powershell-icon">
              <path
                d="M23.181 3.077c.568 0 .923.463.792 1.035l-3.659 15.981c-.13.572-.697 1.035-1.265 1.035H.819c-.568 0-.923-.463-.792-1.035L3.686 4.112c.13-.572.697-1.035 1.265-1.035h18.23zm-8.375 9.345c.251-.394.227-.905-.09-1.243L9.122 5.228c-.38-.405-1.037-.407-1.466-.004-.429.403-.468 1.057-.088 1.461l4.662 4.96v.11l-7.42 5.373c-.45.327-.533.977-.187 1.453.346.476.991.597 1.44.27l8.229-5.909c.28-.197.438-.366.514-.52zm-2.796 4.399a.928.928 0 0 0-.934.923c0 .51.418.923.934.923h4.433a.928.928 0 0 0 .934-.923.928.928 0 0 0-.934-.923H12.01z"
              />
            </g>
            <g id="variables-icon">
              <path
                d="m 6.6666667,21.333333 a 4,4 0 0 1 -4,-4 V 6.6666667 a 4,4 0 0 1 4,-4 A 1.3333334,1.3333334 0 0 0 6.6666667,0 6.6666667,6.6666667 0 0 0 0,6.6666667 V 17.333333 A 6.6666667,6.6666667 0 0 0 6.6666667,24 a 1.3333335,1.3333335 0 0 0 0,-2.666667 z M 16.946667,16.946667 a 1.3333333,1.3333333 0 0 0 0,-1.893334 L 13.88,12 16.946667,8.9466667 A 1.3387891,1.3387891 0 0 0 15.053333,7.0533333 L 12,10.12 8.9466667,7.0533333 A 1.3387889,1.3387889 0 0 0 7.0533333,8.9466667 L 10.12,12 7.0533333,15.053333 a 1.3333333,1.3333333 0 0 0 0,1.893334 1.3333333,1.3333333 0 0 0 1.8933334,0 L 12,13.88 l 3.053333,3.066667 a 1.3333333,1.3333333 0 0 0 1.893334,0 z M 17.333333,0 a 1.3333333,1.3333333 0 0 0 0,2.6666667 4,4 0 0 1 4,4 V 17.333333 a 4,4 0 0 1 -4,4 1.3333335,1.3333335 0 0 0 0,2.666667 A 6.6666667,6.6666667 0 0 0 24,17.333333 V 6.6666667 A 6.6666667,6.6666667 0 0 0 17.333333,0 Z"
                id="path611"
                style="stroke-width:1.33333"
              />
            </g>
          </defs>
        </svg>
      </vaadin-iconset>

      <vaadin-tabs
        orientation="vertical"
        id="tabs"
        style="flex: 1; min-height: 0;"
      >
        <vaadin-tab>
          <a href="${urlForName('deploy')}" @click="${this.openDeploy}">
            <vaadin-icon icon="vaadin:expand-square" theme="small"></vaadin-icon>
            Deploy
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('monitor-requests')}">
            <vaadin-icon icon="vaadin:clipboard" theme="small"></vaadin-icon>
            Monitor
          </a>
        </vaadin-tab>
        ${repeat(
          this.shortcuts.results,
          resultKey,
          result => html`<vaadin-tab
            ><monitor-result-tab
              .requestStatus="${result}"
            ></monitor-result-tab
          ></vaadin-tab>`
        )}
        <vaadin-tab>
          <a href="${urlForName('projects')}">
            <vaadin-icon icon="vaadin:archives" theme="small"></vaadin-icon>
            Projects
          </a>
        </vaadin-tab>
        ${repeat(
          this.shortcuts.projects,
          projectKey,
          project => html`<vaadin-tab
            ><project-envs-tab .project="${project}"></project-envs-tab
          ></vaadin-tab>`
        )}
        <vaadin-tab>
          <a href="${urlForName('environments')}">
            <vaadin-icon icon="vaadin:cubes" theme="small"></vaadin-icon>
            Environments
          </a>
        </vaadin-tab>
        ${repeat(
          this.shortcuts.environments,
          envKey,
          env => html`<vaadin-tab
            ><env-detail-tab .env="${env}"></env-detail-tab
          ></vaadin-tab>`
        )}
        <vaadin-tab>
          <a href="${urlForName('servers')}">
            <vaadin-icon icon="vaadin:server" theme="small"></vaadin-icon>
            Servers
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('databases')}">
            <vaadin-icon icon="vaadin:database" theme="small"></vaadin-icon>
            Databases
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('sql-roles')}" style="padding-left: var(--lumo-space-l);">
              <vaadin-icon icon="vaadin:key" theme="small"></vaadin-icon>
              Roles
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('sql-ports')}" style="padding-left: var(--lumo-space-l);">
              <vaadin-icon icon="vaadin:connect" theme="small"></vaadin-icon>
              Ports
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('users')}">
            <vaadin-icon icon="vaadin:users" theme="small"></vaadin-icon>
            Users
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('daemons')}">
            <vaadin-icon icon="vaadin:cogs" theme="small"></vaadin-icon>
            Daemons
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('scripts')}">
            <vaadin-icon icon="inline:powershell-icon" theme="small"></vaadin-icon>
            Scripts
          </a>
        </vaadin-tab>

        <vaadin-tab>
          <a href="${urlForName('variables')}">
            <vaadin-icon icon="inline:variables-icon" theme="small"></vaadin-icon>
            Variables
          </a>
        </vaadin-tab>

        <vaadin-tab
          aria-expanded="${this.auditMenuExpanded ? 'true' : 'false'}"
          @click="${this._toggleAuditMenu}"
        >
          <a href="#" @click="${(e: Event) => e.preventDefault()}">
            <vaadin-icon icon="vaadin:calendar-user" theme="small"></vaadin-icon>
            Audit
            <vaadin-icon
              icon="${this.auditMenuExpanded ? 'vaadin:chevron-down-small' : 'vaadin:chevron-right-small'}"
              theme="small"
            ></vaadin-icon>
          </a>
        </vaadin-tab>
        <!--
          Audit sub-tabs are rendered always (not conditionally) and hidden via ?hidden when
          the Audit menu is collapsed. This keeps them in the DOM so <vaadin-tabs>'
          setSelectedTab can still locate and highlight the current audit route when the user
          navigates there directly (e.g. via a bookmarked URL).
        -->
        <vaadin-tab ?hidden="${!this.auditMenuExpanded}">
          <a href="${urlForName('scripts-audit')}" title="Scripts Audit">
            <div class="sub-item">
              <vaadin-icon icon="inline:powershell-icon" theme="small"></vaadin-icon>
              Scripts Audit
            </div>
          </a>
        </vaadin-tab>
        <vaadin-tab ?hidden="${!this.auditMenuExpanded}">
          <a href="${urlForName('variables-audit')}" title="Variables Audit">
            <div class="sub-item">
              <vaadin-icon icon="inline:variables-icon" theme="small"></vaadin-icon>
              Variables Audit
            </div>
          </a>
        </vaadin-tab>
        <vaadin-tab ?hidden="${!this.auditMenuExpanded}">
          <a href="${urlForName('projects-audit')}" title="Projects Audit">
            <div class="sub-item">
              <vaadin-icon icon="vaadin:archives" theme="small"></vaadin-icon>
              Projects Audit
            </div>
          </a>
        </vaadin-tab>
        <vaadin-tab ?hidden="${!this.auditMenuExpanded}">
          <a href="${urlForName('daemons-audit')}" title="Daemons Audit">
            <div class="sub-item">
              <vaadin-icon icon="vaadin:cogs" theme="small"></vaadin-icon>
              Daemons Audit
            </div>
          </a>
        </vaadin-tab>
        <vaadin-tab ?hidden="${!this.auditMenuExpanded}">
          <a href="${urlForName('databases-audit')}" title="Databases Audit">
            <div class="sub-item">
              <vaadin-icon icon="vaadin:database" theme="small"></vaadin-icon>
              Databases Audit
            </div>
          </a>
        </vaadin-tab>
        <vaadin-tab ?hidden="${!this.auditMenuExpanded}">
          <a href="${urlForName('servers-audit')}" title="Servers Audit">
            <div class="sub-item">
              <vaadin-icon icon="vaadin:server" theme="small"></vaadin-icon>
              Servers Audit
            </div>
          </a>
        </vaadin-tab>
        ${this.isAdmin
          ? html`
              <vaadin-tab>
                <a href="${urlForName('configuration')}">
                  <vaadin-icon icon="vaadin:options" theme="small"></vaadin-icon>
                  Configuration
                </a>
              </vaadin-tab>
            `
          : html``}
        <vaadin-tab>
          <a href="${urlForName('analytics')}">
            <vaadin-icon icon="vaadin:chart" theme="small"></vaadin-icon>
            Analytics
          </a>
        </vaadin-tab>
      </vaadin-tabs>
      <div
        style="padding: var(--lumo-space-xs); text-align: center; color: var(--dorc-text-secondary-strong); font-size: var(--lumo-font-size-xs); flex-shrink: 0;"
      >
        ${this.metaData}
      </div>
    `;
  }
  constructor() {
    super();
    this.getUserRoles();
    this.getMetaData();
  }

  connectedCallback() {
    super.connectedCallback();
    drawerShortcuts.start();
    this.shortcuts = drawerShortcuts.snapshot();
    // The store is the single source of truth; this component renders it.
    this.unsubscribe = drawerShortcuts.subscribe(() => {
      this.shortcuts = drawerShortcuts.snapshot();
    });
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    // D-27: the previous implementation registered a visibilitychange listener
    // in firstUpdated as an anonymous arrow with no stored reference and no
    // disconnectedCallback, so a detached navbar kept parsing cookies and
    // inserting tabs into its orphaned shadow root forever.
    this.unsubscribe?.();
    this.unsubscribe = undefined;
  }

  private _toggleAuditMenu(event: Event) {
    event.preventDefault();
    event.stopPropagation();
    this.auditMenuExpanded = !this.auditMenuExpanded;
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

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'close-env-detail',
      this.closeEnvDetail as EventListener
    );
    this.addEventListener(
      'close-monitor-result',
      this.closeMonitorResult as EventListener
    );
    this.addEventListener(
      'close-project-envs',
      this.closeProjectEnvs as EventListener
    );
  }

  private getMetaData() {
    const api = new MetadataApi(dorcApiConfiguration);
    api.metadataGet().subscribe({
      next: (data: string) => {
        this.metaData = data;
      },
      error: (err: string) => console.error(err)
    });
  }

  private openDeploy() {
    if (location.pathname === '/deploy') {
      location.reload();
    }
  }

  updated() {
    this.setSelectedTab(window.location.pathname);
  }

  // ── Close handlers ───────────────────────────────────────────────────────
  // Thin delegates. The store owns removal, de-duplication and persistence, so
  // "close" can no longer disagree with "open" about what identity means — the
  // disagreement that produced duplicate tabs after renames and shortcuts that
  // resurrected on reload.

  public closeProjectEnvs(e: CustomEvent) {
    const proj = e.detail.Project as ProjectApiModel;
    this.withFocusKept(() =>
      drawerShortcuts.remove('projects', toProjectShortcut(proj))
    );
  }

  public closeMonitorResult(e: CustomEvent) {
    const req = e.detail.request as DeploymentRequestApiModel;
    this.withFocusKept(() =>
      drawerShortcuts.remove('results', toResultShortcut(req))
    );
  }

  public closeEnvDetail(e: CustomEvent) {
    const env = e.detail.Environment as EnvironmentApiModel;
    this.withFocusKept(() =>
      drawerShortcuts.remove('environments', toEnvShortcut(env))
    );
  }

  public renameEnvDetail(e: CustomEvent) {
    const oldName = e.detail.oldName as string;
    const newEnv = e.detail.environment as EnvironmentApiModel;
    drawerShortcuts.renameEnvironment(oldName, toEnvShortcut(newEnv));
  }

  /**
   * Runs a removal, then puts focus somewhere sensible (SC-4a).
   *
   * Removing the shortcut destroys the element that had focus, which otherwise
   * drops the user on <body> — a keyboard user closing a second shortcut would
   * have to traverse the whole drawer again. Only acts when focus was inside the
   * tab being removed, so a mouse click does not yank focus around.
   */
  private withFocusKept(mutate: () => void) {
    const active = this.deepActiveElement();
    const tab = active?.closest('vaadin-tab') as HTMLElement | null;
    const next = (tab?.nextElementSibling ??
      tab?.previousElementSibling) as HTMLElement | null;

    mutate();

    if (tab && next) {
      // Re-render removes the tab; move focus once that has settled.
      this.updateComplete.then(() => {
        if (next.isConnected) next.focus();
      });
    }
  }

  /** activeElement, followed down through shadow roots. */
  private deepActiveElement(): Element | null {
    let el: Element | null =
      this.shadowRoot?.activeElement ?? document.activeElement;
    while (el?.shadowRoot?.activeElement) {
      el = el.shadowRoot.activeElement;
    }
    return el;
  }

  private getProjectEnvsPath(project: ProjectShortcut) {
    return `/project-envs/${encodeURIComponent(project.ProjectName)}`;
  }

  private getMonitorResultPath(result: ResultShortcut) {
    return `/monitor-result/${result.Id}`;
  }

  private getEnvDetailPath(env: EnvShortcut) {
    return `/environment/${encodeURIComponent(env.EnvironmentName)}/${
      EnvPageTabNames.Metadata
    }`;
  }

  public setSelectedTab(path: string) {
    const tabs = this.shadowRoot?.getElementById('tabs') as Tabs;
    if (tabs) {
      tabs.selected = this.getIndexOfPath(tabs, path);
    }
  }

  private getIndexOfPath(tabs: Tabs, path: string) {
    const tabsArray = [].slice.call(tabs.children) as Tab[];

    let i;
    let idx = -1;
    for (i = 0; i < tabsArray.length; i += 1) {
      const tab = tabsArray[i] as Tab;
      let childPath = '';
      // An environment shortcut should stay highlighted across ALL of that
      // environment's sub-tabs, so it matches on this prefix rather than on the
      // one exact path getEnvDetailPath happens to build. The trailing slash is
      // what stops "/environment/Foo/" also matching "/environment/FooBar/".
      let envPrefix = '';
      const tabChild = tab.children[0] as unknown as URL;
      // The Audit parent tab uses href="#"; HTMLAnchorElement.pathname resolves "#" against the
      // current document URL, so without this skip every audit sub-route would match the parent.
      const rawHref = (tab.children[0] as Element)?.getAttribute?.('href');
      if (rawHref === '#') {
        continue;
      }
      if (tabChild.pathname === undefined) {
        const envDetailTab = tab.children[0] as EnvDetailTab;
        if (envDetailTab.env !== undefined) {
          childPath = this.getEnvDetailPath(envDetailTab.env);
          envPrefix = `/environment/${String(
            envDetailTab.env.EnvironmentName
          )}/`.toLowerCase();
        }
        const projectEnvsTab = tab.children[0] as ProjectEnvsTab;
        if (projectEnvsTab.project !== undefined) {
          childPath = this.getProjectEnvsPath(projectEnvsTab.project);
        }
        const monitorResultTab = tab.children[0] as MonitorResultTab;
        if (monitorResultTab.requestStatus !== undefined) {
          childPath = this.getMonitorResultPath(monitorResultTab.requestStatus);
        }
      } else {
        childPath = tabChild.pathname;
      }
      // D-12: an environment named e.g. "Perf 100% Load" made this throw
      // URIError, and because getIndexOfPath runs from updated() it re-threw on
      // every render. Paths are now built with encodeURIComponent, but a
      // hand-typed or bookmarked URL can still be malformed.
      const normalizePath = (value: string) => {
        try {
          return decodeURIComponent(value).toLowerCase();
        } catch {
          return value.toLowerCase();
        }
      };
      const pathCorrected = normalizePath(path);
      const childPathCorrected = normalizePath(childPath);
      if (pathCorrected === '/') {
        idx = 0;
        break;
      }
      if (
        pathCorrected === childPathCorrected ||
        (envPrefix !== '' && pathCorrected.startsWith(envPrefix))
      ) {
        idx = tabsArray.indexOf(tab);
        break;
      }
    }
    return idx;
  }
}
