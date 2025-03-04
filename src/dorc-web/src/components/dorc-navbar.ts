import '@vaadin/icons/vaadin-icons';
import '@vaadin/icons/vaadin-iconset.js';
import '@vaadin/icon';
import '@vaadin/tabs';
import { Tabs } from '@vaadin/tabs';
import { Tab } from '@vaadin/tabs/vaadin-tab';
import '@vaadin/vertical-layout';
import { css, html, LitElement, PropertyValues, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import {
  DeploymentRequestApiModel,
  EnvironmentApiModel,
  MetadataApi,
  ProjectApiModel
} from '../apis/dorc-api';
import '../helpers/cookies';
import { deleteCookie, getCookie, setCookie } from '../helpers/cookies';
import { urlForName } from '../router/router';
import './tabs/env-detail-tab';
import { EnvDetailTab } from './tabs/env-detail-tab';
import './tabs/project-envs-tab';
import { ProjectEnvsTab } from './tabs/project-envs-tab';
import './tabs/monitor-result-tab';
import { MonitorResultTab } from './tabs/monitor-result-tab';
import GlobalCache from '../global-cache.ts';
import { EnvPageTabNames } from '../pages/page-environment.ts';

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
        background-color: #eee;
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
        color: blue;
      }

      vaadin-tab {
        padding-top: 0px;
        padding-bottom: 0px;
      }
    `;
  }

  openEnvTabs: EnvironmentApiModel[] = [];
  openProjTabs: ProjectApiModel[] = [];
  openResultTabs: DeploymentRequestApiModel[] = [];

  private monitorResultTabs = 'monitor-result-tabs';
  private envDetailTabs = 'env-detail-tabs';
  private projectEnvsTabs = 'project-envs-tabs';

  public userRoles!: string[];

  @property() metaData = '';

  @property({ type: Boolean }) isAdmin = false;

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
        style="height: calc(100vh - 60px);"
      >
        <vaadin-tab>
          <a href="${urlForName('deploy')}" @click="${this.openDeploy}">
            <vaadin-icon icon="vaadin:expand-square"></vaadin-icon>
            Deploy
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('monitor-requests')}">
            <vaadin-icon icon="vaadin:clipboard"></vaadin-icon>
            Monitor
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('projects')}">
            <vaadin-icon icon="vaadin:archives"></vaadin-icon>
            Projects
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('environments')}">
            <vaadin-icon icon="vaadin:cubes"></vaadin-icon>
            Environments
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('servers')}">
            <vaadin-icon icon="vaadin:server"></vaadin-icon>
            Servers
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('databases')}">
            <vaadin-icon icon="vaadin:database"></vaadin-icon>
            Databases
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('sql-roles')}">
            <div style="margin-left: 20px; width: 210px">
              <vaadin-icon icon="vaadin:key"></vaadin-icon>
              Roles
            </div>
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('sql-ports')}">
            <div style="margin-left: 20px; width: 210px">
              <vaadin-icon icon="vaadin:connect"></vaadin-icon>
              Ports
            </div>
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('users')}">
            <vaadin-icon icon="vaadin:users"></vaadin-icon>
            Users
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('daemons')}">
            <vaadin-icon icon="vaadin:cogs"></vaadin-icon>
            Daemons
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('scripts')}">
            <vaadin-icon icon="inline:powershell-icon"></vaadin-icon>
            Scripts
          </a>
        </vaadin-tab>
        <vaadin-tab>
          <a href="${urlForName('variables')}">
            <vaadin-icon icon="inline:variables-icon"></vaadin-icon>
            Variables
          </a>
        </vaadin-tab>

        <vaadin-tab>
          <a href="${urlForName('variables-audit')}">
            <div style="margin-left: 20px; width: 210px">
              <vaadin-icon icon="vaadin:calendar-user"></vaadin-icon>
              Audit
            </div>
          </a>
        </vaadin-tab>
        ${this.isAdmin
          ? html`
              <vaadin-tab>
                <a href="${urlForName('configuration')}">
                  <vaadin-icon icon="vaadin:options"></vaadin-icon>
                  Configuration
                </a>
              </vaadin-tab>
            `
          : html``}
        <vaadin-tab>
          <a href="${urlForName('about')}">
            <vaadin-icon icon="vaadin:at"></vaadin-icon>
            About
          </a>
        </vaadin-tab>
      </vaadin-tabs>
      <div
        style="position: fixed; top: calc(100% - 13px); text-align: center; left: 50px; color: #747f8d; font-size: x-small"
      >
        ${this.metaData}
      </div>
    `;
  }

  constructor() {
    super();

    GlobalCache.getInstance().allRolesResp?.subscribe((data: Array<string>) => {
      this.userRoles = data;
      this.isAdmin = this.userRoles.find(p => p === 'Admin') !== undefined;
    });

    this.getMetaData();
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

    this.loadFromEnvDetailCookie();
    this.loadFromProjEnvsCookie();
    this.loadFromMonitorResultsCookie();
  }

  loadFromEnvDetailCookie() {
    const envTabs = getCookie(this.envDetailTabs) as string;
    if (envTabs !== undefined) {
      if (envTabs === '') return;
      try {
        this.openEnvTabs = JSON.parse(envTabs) as EnvironmentApiModel[];

        this.openEnvTabs.forEach(value => this.insertEnvTab(value));
      } catch {
        deleteCookie(this.envDetailTabs);
      }
    }
  }

  loadFromProjEnvsCookie() {
    const projTabs = getCookie(this.projectEnvsTabs) as string;
    if (projTabs !== undefined) {
      if (projTabs === '') return;
      try {
        this.openProjTabs = JSON.parse(projTabs) as ProjectApiModel[];

        this.openProjTabs.forEach(value => this.insertProjTab(value));
      } catch {
        deleteCookie(this.projectEnvsTabs);
      }
    }
  }

  loadFromMonitorResultsCookie() {
    const resultTabs = getCookie(this.monitorResultTabs) as string;
    if (resultTabs !== undefined) {
      if (resultTabs === '') return;
      try {
        this.openResultTabs = JSON.parse(
          resultTabs
        ) as DeploymentRequestApiModel[];

        this.openResultTabs.forEach(value => this.insertResultTab(value));
      } catch {
        deleteCookie(this.monitorResultTabs);
      }
    }
  }

  private getMetaData() {
    const api = new MetadataApi();
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

  public closeProjectEnvs(e: CustomEvent) {
    const proj = e.detail.Project as ProjectApiModel;
    for (let i = 0; i < this.openProjTabs.length; i += 1) {
      if (this.openProjTabs[i].ProjectId === proj.ProjectId) {
        this.openProjTabs.splice(i, 1);
      }
    }

    const tabs = this.shadowRoot?.getElementById('tabs') as Tabs;
    const path = this.getProjectEnvsPath(proj);
    const idx = this.getIndexOfPath(tabs, path);
    const tabsArray = [].slice.call(tabs.children) as Tab[];
    tabs.removeChild(tabsArray[idx]);
    setCookie(this.projectEnvsTabs, JSON.stringify(this.openProjTabs));
  }

  public closeMonitorResult(e: CustomEvent) {
    const req = e.detail.request as DeploymentRequestApiModel;
    for (let i = 0; i < this.openResultTabs.length; i += 1) {
      if (this.openResultTabs[i].Id === req.Id) {
        this.openResultTabs.splice(i, 1);
      }
    }

    const tabs = this.shadowRoot?.getElementById('tabs') as Tabs;
    const path = this.getMonitorResultPath(req);
    const idx = this.getIndexOfPath(tabs, path);
    const tabsArray = [].slice.call(tabs.children) as Tab[];
    tabs.removeChild(tabsArray[idx]);
    setCookie(this.monitorResultTabs, JSON.stringify(this.openResultTabs));
  }

  public closeEnvDetail(e: CustomEvent) {
    const env = e.detail.Environment as EnvironmentApiModel;
    for (let i = 0; i < this.openEnvTabs.length; i += 1) {
      if (this.openEnvTabs[i].EnvironmentId === env.EnvironmentId) {
        this.openEnvTabs.splice(i, 1);
      }
    }

    const tabs = this.shadowRoot?.getElementById('tabs') as Tabs;
    const path = this.getEnvDetailPath(env);
    const idx = this.getIndexOfPath(tabs, path);
    const tabsArray = [].slice.call(tabs.children) as Tab[];
    tabs.removeChild(tabsArray[idx]);
    setCookie(this.envDetailTabs, JSON.stringify(this.openEnvTabs));
  }

  public insertProjTab(projectAPIModel: ProjectApiModel) {
    const tabs = this.shadowRoot?.getElementById('tabs') as Tabs;

    const tab = new Tab();
    render(
      html` <project-envs-tab
        .project="${projectAPIModel}"
      ></project-envs-tab>`,
      tab
    );

    const path = this.getProjectEnvsPath(projectAPIModel);
    tabs.insertBefore(
      tab,
      tabs.children[this.getIndexOfPath(tabs, '/environments')]
    );
    return path;
  }

  private getProjectEnvsPath(projectAPIModel: ProjectApiModel) {
    return `/project-envs/${String(projectAPIModel.ProjectName)}`;
  }

  public insertResultTab(requestStatus: DeploymentRequestApiModel) {
    const tabs = this.shadowRoot?.getElementById('tabs') as Tabs;

    const tab = new Tab();
    render(
      html` <monitor-result-tab
        .requestStatus="${requestStatus}"
      ></monitor-result-tab>`,
      tab
    );

    const path = this.getMonitorResultPath(requestStatus);
    tabs.insertBefore(
      tab,
      tabs.children[this.getIndexOfPath(tabs, '/projects')]
    );
    return path;
  }

  public insertEnvTab(env: EnvironmentApiModel) {
    const tabs = this.shadowRoot?.getElementById('tabs') as Tabs;

    const tab = new Tab();
    render(html` <env-detail-tab .env="${env}"></env-detail-tab>`, tab);

    tabs.insertBefore(
      tab,
      tabs.children[this.getIndexOfPath(tabs, '/servers')]
    );
  }

  private getMonitorResultPath(result: DeploymentRequestApiModel) {
    return `/monitor-result/${String(result.Id)}`;
  }

  private getEnvDetailPath(env: EnvironmentApiModel) {
    return `/environment/${String(env.EnvironmentName)}/${EnvPageTabNames.Metadata}`;
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
      const tabChild = tab.children[0] as unknown as URL;
      if (tabChild.pathname === undefined) {
        const envDetailTab = tab.children[0] as EnvDetailTab;
        if (envDetailTab.env !== undefined) {
          childPath = this.getEnvDetailPath(envDetailTab.env);
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
      const pathCorrected = decodeURIComponent(path.toLowerCase());
      const childPathCorrected = childPath.toLowerCase();
      if (pathCorrected === '/') {
        idx = 0;
        break;
      }
      if (pathCorrected === childPathCorrected) {
        idx = tabsArray.indexOf(tab);
        break;
      }
    }
    return idx;
  }
}
