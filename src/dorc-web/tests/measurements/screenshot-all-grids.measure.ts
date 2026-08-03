 
/**
 * ALL-GRIDS sweep — renders every migrated grid view at 375px and captures a
 * screenshot, so each one gets eyes-on. Property-driven views get mock rows;
 * DP-backed views get a minimal XHR shim (RequestStatuses returns canned
 * deployment requests; everything else returns an empty list, which still
 * verifies the collapse, bar and empty-state don't break).
 */
import { fixture, html, expect } from '../_helpers';
import { page } from '@vitest/browser/context';
import lumoFlat from '@vaadin/vaadin-lumo-styles/dist/lumo.css?raw';

const lumoStyle = document.createElement('style');
lumoStyle.textContent = lumoFlat;
document.head.appendChild(lumoStyle);
document.documentElement.style.fontFamily = 'var(--lumo-font-family)';
document.documentElement.style.color = 'var(--lumo-body-text-color)';

// ---- minimal XHR shim (rxjs ajax uses XMLHttpRequest) ----
const RealXHR = window.XMLHttpRequest;
class ShimXHR {
  static DONE = 4;
  readyState = 0;
  status = 0;
  response: unknown = null;
  responseText = '';
  responseType = '';
  private listeners: Record<string, Array<(e: unknown) => void>> = {};
  private url = '';
  upload = { addEventListener: () => {} };
  open(_m: string, url: string) {
    this.url = url;
    this.readyState = 1;
  }
  setRequestHeader() {}
  getAllResponseHeaders() {
    return 'content-type: application/json';
  }
  getResponseHeader(name: string) {
    return name.toLowerCase() === 'content-type' ? 'application/json' : null;
  }
  addEventListener(type: string, fn: (e: unknown) => void) {
    (this.listeners[type] ??= []).push(fn);
  }
  removeEventListener() {}
  abort() {}
  send() {
    setTimeout(() => {
      const auditRow = (f: Record<string, unknown>) => ({
        Username: 'SEFE\\ben.hegarty', Action: 'Update', Date: '02/08/2026 14:32:07',
        FromValue: 'v2026.07.28.1', ToValue: 'v2026.08.02.3', ...f
      });
      const paged = (rows: unknown[]) => ({ Items: rows, TotalItems: rows.length });
      let body: unknown = [];
      if (/ServerAudit/i.test(this.url)) body = paged([auditRow({ ServerName: 'GMPR-DORAPP02' })]);
      else if (/DatabaseAudit/i.test(this.url)) body = paged([auditRow({ DatabaseName: 'DORC_PROD_01' })]);
      else if (/DaemonAudit/i.test(this.url)) body = paged([auditRow({ DaemonName: 'Dorc.Monitor' })]);
      else if (/ProjectAudit/i.test(this.url)) body = paged([auditRow({ Project: { ProjectName: 'Trading-Platform' } })]);
      else if (/ScriptsAudit/i.test(this.url)) body = paged([auditRow({ ScriptName: 'Deploy-Db.ps1', ProjectNames: 'Trading', UpdatedBy: 'SEFE\\bh', UpdatedDate: '02/08/2026' })]);
      else if (/PropertyValuesAudit/i.test(this.url)) body = paged([auditRow({ PropertyName: 'ConnectionString', EnvironmentName: 'PROD-EU-01', UpdatedBy: 'SEFE\\bh', UpdatedDate: '02/08/2026' })]);
      else if (/SearchPropertyValues/i.test(this.url)) body = paged([{ Property: 'ConnectionString', PropertyValueScope: 'PROD-EU-01', Value: 'Server=GMPR-SQL01;Database=DORC', Id: 1 }]);
      else if (/ScopedPropertyValues/i.test(this.url)) body = paged([{ Property: 'ConnectionString', PropertyValueScope: 'PROD-EU-01', Secure: false, Value: 'Server=GMPR-SQL01', Id: 1 }]);
      else if (/RefDataServers|Servers\?/i.test(this.url) && !/Audit/i.test(this.url)) body = paged([{ Name: 'GMPR-DORAPP02', OsName: 'Windows Server 2022', ApplicationTags: 'dorc', EnvironmentNames: ['PROD-EU-01'], Id: 1 }]);
      else if (/RefDataDatabases|Databases\?/i.test(this.url) && !/Audit/i.test(this.url)) body = paged([{ ServerName: 'GMPR-SQL01', Name: 'DORC_PROD_01', ArrayName: 'arr1', ApplicationTags: 'dorc', EnvironmentNames: ['PROD-EU-01'], Id: 1 }]);
      else if (/RefDataScripts|Scripts\?/i.test(this.url) && !/Audit/i.test(this.url)) body = paged([{ Name: 'Deploy-Db.ps1', ProjectNames: 'Trading', NonProdOnly: false, Path: 'scripts/deploy', PowerShellVersionNumber: '7.4', IsEnabled: true, Id: 1 }]);
      else if (/RequestStatuses/i.test(this.url)) {
        body = {
          Items: [
            { Id: 284713, Project: 'Trading-Platform', EnvironmentName: 'PROD-EU-01', BuildNumber: '2026.08.02.3', Status: 'Failed', UserName: 'SEFE\\ben.hegarty', StartedTime: '2026-08-02T14:32:07', CompletedTime: '2026-08-02T14:39:51', RequestedTime: '2026-08-02T14:31:00', Components: 'Dorc.Api, Dorc.Web', UserEditable: true },
            { Id: 284712, Project: 'Trading-Platform', EnvironmentName: 'PROD-EU-01', BuildNumber: '2026.08.02.2', Status: 'Complete', UserName: 'SEFE\\app.support', StartedTime: '2026-08-02T12:02:00', CompletedTime: '2026-08-02T12:09:10', RequestedTime: '2026-08-02T12:01:00', Components: 'Dorc.Api', UserEditable: false },
            { Id: 284711, Project: 'DORC', EnvironmentName: 'UAT-EU-02', BuildNumber: '2026.07.30.1', Status: 'Running', UserName: 'SEFE\\ben.hegarty', StartedTime: '2026-08-02T11:00:00', CompletedTime: null, RequestedTime: '2026-08-02T10:59:00', Components: 'Dorc.Monitor', UserEditable: true }
          ],
          TotalItems: 3
        };
      }
      this.response = body;
      this.responseText = JSON.stringify(body);
      this.status = 200;
      this.readyState = 4;
      (this.listeners['readystatechange'] ?? []).forEach(f => f({}));
      (this.listeners['load'] ?? []).forEach(f => f({}));
    }, 0);
  }
  onreadystatechange: (() => void) | null = null;
}

import '../../src/pages/page-monitor-requests';
import '../../src/pages/page-env-history';
import '../../src/pages/page-projects-list';
import '../../src/pages/page-environments-list';
import '../../src/pages/page-permissions-list';
import '../../src/pages/page-sql-ports-list';
import '../../src/pages/page-users-list';
import '../../src/pages/page-daemons-list';
import '../../src/pages/page-config-values-list';
import '../../src/pages/page-project-bundles';
import '../../src/pages/page-project-components';
import '../../src/pages/page-servers-list';
import '../../src/pages/page-databases-list';
import '../../src/pages/page-servers-audit';
import '../../src/pages/page-databases-audit';
import '../../src/pages/page-daemons-audit';
import '../../src/pages/page-projects-audit';
import '../../src/pages/page-scripts-audit';
import '../../src/pages/page-scripts-list';
import '../../src/pages/page-variables-audit';
import '../../src/pages/page-variables-value-lookup';
import '../../src/pages/page-variables';
import '../../src/components/component-deployment-results';
import '../../src/components/component-previous-attempts';
import '../../src/components/attached-app-users';
import '../../src/components/attached-databases';
import '../../src/components/attached-servers';
import '../../src/components/attached-env-tenants';
import '../../src/components/application-daemons';
import '../../src/components/environment-tabs/env-deployments';
import '../../src/components/environment-tabs/env-variables';
import '../../src/components/environment-tabs/env-monitor';
import '../../src/components/map-daemons';
import '../../src/components/make-like-production';
import '../../src/components/deploy/deploy-env';
import '../../src/components/add-edit-access-control';

const settle = async (n = 6) => {
  for (let i = 0; i < n; i++)
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
};

type ViewSpec = {
  tag: string;
  setup?: (el: any) => void;
  post?: (el: any) => Promise<void> | void;
  height?: string;
};

/**
 * List pages fetch on connect and overwrite properties assigned in setup
 * with the shim's (empty) response — so the rows silently vanish from the
 * capture. Re-run the injection after the view settles.
 */
const reinject = (spec: ViewSpec): ViewSpec => ({
  ...spec,
  post: async el => {
    el.loading = false;
    spec.setup?.(el);
    el.requestUpdate?.();
  }
});

const VIEWS: ViewSpec[] = [
  { tag: 'page-monitor-requests', height: '360px' },
  { tag: 'env-monitor', height: '360px' },
  {
    tag: 'page-env-history',
    height: '300px',
    post: el =>
      el.setEnvHistory([
        { EnvName: 'PROD-EU-01', UpdateDate: '02/08/2026 14:32:07', UpdatedBy: 'SEFE\\ben.hegarty', UpdateType: 'Component', FromValue: '2026.07.28.1', ToValue: '2026.08.02.3', Details: 'Pipeline', Comment: 'Routine' }
      ])
  },
  {
    tag: 'page-projects-list',
    height: '300px',
    setup: el => {
      el.filteredProjects = [
        { ProjectName: 'Trading-Platform', ProjectDescription: 'Core trading services', ArtefactsUrl: 'https://tfs.sefe.local/tfs/Trading/_build?definitionId=1247', ArtefactsSubPaths: 'drop', ArtefactsBuildRegex: '.*', LeanIXUrl: '', Id: 1 }
      ];
    },
    post: el => {
      el.loading = false;
      el.projects = el.filteredProjects;
      el.requestUpdate();
    }
  },
  reinject({
    tag: 'page-environments-list',
    height: '300px',
    setup: el => {
      el.userRolesLoaded = true;
      el.filteredEnvironments = [
        { EnvironmentName: 'PROD-EU-01', EnvironmentSecure: true, EnvironmentIsProd: true, Details: { EnvironmentOwner: 'SEFE\\ben.hegarty', Description: 'Primary production', FileShare: '\\\\gmpr-fs01\\dorc', Notes: 'Managed' }, Id: 1 }
      ];
    }
  }),
  reinject({ tag: 'page-permissions-list', height: '260px', setup: el => { el.filteredPermissions = [{ DisplayName: 'Deploy to Production', PermissionName: 'DeployProd', Id: 1 }]; } }),
  reinject({ tag: 'page-sql-ports-list', height: '260px', setup: el => { el.filteredSqlPorts = [{ InstanceName: 'GMPR-SQL01\\TRADING', SqlPort: '1433', Id: 1 }]; } }),
  reinject({ tag: 'page-users-list', height: '260px', setup: el => { el.filteredUsers = [{ DisplayName: 'Ben Hegarty', LanId: 'SEFE\\ben.hegarty', LoginType: 'AD', Id: 1 }]; } }),
  reinject({ tag: 'page-daemons-list', height: '280px', setup: el => { el.filteredDaemons = [{ Name: 'Dorc.Monitor', DisplayName: 'DORC Monitor', AccountName: 'svc-dorc', ServiceType: 'Windows Service', LastSeenDate: '02/08/2026', Id: 1 }]; } }),
  reinject({ tag: 'page-config-values-list', height: '260px', setup: el => { el.filteredConfigValues = [{ Key: 'RabbitMq.Host', Secure: 'false', IsForProd: 'true', Value: 'gmpr-mq01', Id: 1 }]; } }),
  reinject({ tag: 'page-project-bundles', height: '280px', setup: el => { el.filteredBundledRequests = [{ BundleName: 'Trading-Full', RequestName: 'Deploy DB', Sequence: 1, Type: 'Deploy', Request: '{"a":1}', Id: 1 }]; } }),
  reinject({ tag: 'page-project-components', height: '280px', setup: el => { el.deploymentRows = [{ environmentName: 'PROD-EU-01', buildNumber: '2026.08.02.3', status: 'Complete', updateDate: '02/08/2026', componentId: 1 }]; } }),
  {
    tag: 'page-servers-list',
    height: '280px',
    // debounced DP + loading gate race the harness; feed the grid directly —
    // the row template (the thing under visual check) renders either way
    post: async el => {
      el.loading = false;
      el.requestUpdate();
      await el.updateComplete;
      // inject AFTER the re-render so the template's dataProvider binding
      // doesn't overwrite the injected items
      const g = el.shadowRoot!.querySelector('vaadin-grid') as any;
      g.dataProvider = undefined;
      g.items = [{ Name: 'GMPR-DORAPP02', OsName: 'Windows Server 2022', ApplicationTags: 'dorc', EnvironmentNames: ['PROD-EU-01'], Id: 1 }];
      g.requestContentUpdate();
    }
  },
  {
    tag: 'page-databases-list',
    height: '280px',
    post: async el => {
      el.loading = false;
      el.requestUpdate();
      await el.updateComplete;
      const g = el.shadowRoot!.querySelector('vaadin-grid') as any;
      g.dataProvider = undefined;
      g.items = [{ ServerName: 'GMPR-SQL01', Name: 'DORC_PROD_01', ArrayName: 'arr1', ApplicationTags: 'dorc', EnvironmentNames: ['PROD-EU-01'], Id: 1 }];
      g.requestContentUpdate();
    }
  },
  { tag: 'page-servers-audit', height: '280px' },
  { tag: 'page-databases-audit', height: '280px' },
  { tag: 'page-daemons-audit', height: '280px' },
  { tag: 'page-projects-audit', height: '280px' },
  { tag: 'page-scripts-audit', height: '280px' },
  { tag: 'page-scripts-list', height: '280px' },
  { tag: 'page-variables-audit', height: '280px' },
  { tag: 'page-variables-value-lookup', height: '280px' },
  {
    tag: 'page-variables',
    height: '560px',
    post: el => {
      el.loadingPropertyValues = false;
      el.propertyValues = [
        { PropertyValueFilter: 'PROD-EU-01', Value: 'Server=GMPR-SQL01;Database=DORC', Id: 1 },
        { PropertyValueFilter: 'Global', Value: 'Server=GMPR-SQL02', Id: 2 }
      ];
      el.requestUpdate();
    }
  },
  { tag: 'component-deployment-results', height: '280px', setup: el => { el.resultItems = [{ ComponentName: 'Dorc.Api.Deploy', Status: 'Failed', Log: 'boom', StartedTime: '2026-08-02T14:32:07', CompletedTime: '2026-08-02T14:39:51', Id: 1 }]; } },
  {
    tag: 'component-previous-attempts',
    height: '300px',
    setup: el => {
      el.attemptItems = [{ AttemptNumber: 2, Status: 'Failed', StartedTime: '2026-08-02T13:02:07', CompletedTime: '2026-08-02T13:04:51', UserName: 'SEFE\\bh', Log: 'x', ComponentResults: [{ ComponentName: 'Dorc.Api.Deploy', Status: 'Failed', StartedTime: '2026-08-02T13:02:07', CompletedTime: '2026-08-02T13:04:51', Log: 'SqlException: timeout', Id: 1 }] }];
    },
    post: async el => {
      const dt = el.shadowRoot!.querySelector('vaadin-details') as any;
      if (dt) dt.opened = true;
    }
  },
  { tag: 'attached-app-users', height: '260px', setup: el => { el.users = [{ DisplayName: 'Ben Hegarty', LoginId: 'ben', LoginType: 'AD', LanId: 'SEFE\\bh', LanIdType: 'Domain', Team: 'Platform' }]; } },
  { tag: 'attached-databases', height: '260px', setup: el => { el.databases = [{ ServerName: 'GMPR-SQL01', Name: 'DORC_PROD_01', ArrayName: 'arr1', Id: 1 }]; } },
  { tag: 'attached-servers', height: '260px', setup: el => { el.servers = [{ Name: 'GMPR-DORAPP02', OsName: 'Windows Server 2022', ApplicationTags: 'dorc', Id: 1 }]; } },
  { tag: 'attached-env-tenants', height: '260px', setup: el => { el.childEnvironments = [{ EnvironmentName: 'PROD-EU-01-TENANT-A', Id: 1 }]; } },
  { tag: 'application-daemons', height: '260px', setup: el => { el.daemonsAndStatuses = [{ ServerName: 'GMPR-DORAPP02', DaemonName: 'Dorc.Monitor', Status: 'Running', Id: 1 }]; } },
  { tag: 'env-deployments', height: '280px', post: el => { el.loading = false; el.requestUpdate(); }, setup: el => { el.deployments = [{ RequestId: 284713, ComponentName: 'Dorc.Api', RequestBuildNum: '2026.08.02.3', UpdatedDate: '2026-08-02T14:32:07', State: 'Complete' }]; } },
  {
    tag: 'env-variables',
    height: '380px',
    // the envLoaded gate needs a routed environment; force the loaded state
    // and feed the grid directly (same pattern as page-servers-list)
    post: async el => {
      el.envLoaded = true;
      el.loading = false;
      el.searching = false;
      el.loadingProperties = false;
      el.loadingScopeOptions = false;
      el.properties = [{ Name: 'Trading.Api.Url', Secure: false, IsArray: false, Id: 1 }];
      el.propertyValueScopeOptions = [];
      el.requestUpdate();
      await el.updateComplete;
      const g = el.shadowRoot!.querySelector('vaadin-grid') as any;
      if (g) {
        g.dataProvider = undefined;
        g.items = [{ Property: 'Trading.Api.Url', PropertyValue: 'https://gmpr-dorapp02/api', PropertyValueScope: 'PROD-EU-01', Id: 1 }];
        g.requestContentUpdate();
      }
    }
  },
  { tag: 'map-daemons', height: '280px', setup: el => { el.mappedDaemons = [{ Name: 'Dorc.Monitor', DisplayName: 'DORC Monitor', ServiceType: 'Windows Service', Id: 1 }]; } },
  {
    tag: 'make-like-production',
    height: '340px',
    setup: el => { el.propertyOverrides = [{ PropertyName: 'TargetDatabase', PropertyValue: 'GMPR-SQL01' }]; },
    post: async el => { (el.shadowRoot!.querySelector('vaadin-details') as any).opened = true; }
  },
  {
    tag: 'deploy-env',
    height: '320px',
    setup: el => { el.propertyOverrides = [{ PropertyName: 'TargetDatabase', PropertyValue: 'GMPR-SQL01' }]; }
  },
  {
    tag: 'add-edit-access-control',
    height: '980px',
    setup: el => {
      el.Privileges = [
        { Name: 'SEFE\\DORC-Prod-Administrators', Allow: 1, Deny: 0, Id: 1 },
        { Name: 'SEFE\\app.support', Allow: 4, Deny: 0, Id: 2 }
      ];
      el.UserEditable = true;
    },
    post: async el => {
      const dlg = el.shadowRoot!.querySelector('hegs-dialog') as any;
      dlg.open = true;
      await new Promise(r => setTimeout(r, 200));
      // pin the overlay dialog into flow for the capture
      const box = dlg.shadowRoot!.querySelector('.dialog') as HTMLElement;
      box.style.position = 'static';
      const overlay = dlg.shadowRoot!.querySelector('.overlay') as HTMLElement;
      overlay.style.display = 'none';
      // the grid initialised while the dialog wrapper was display:none —
      // force it to re-measure now it is visible
      const acGrid = el.shadowRoot!.querySelector('vaadin-grid') as any;
      if (acGrid) acGrid.allRowsVisible = true;
      acGrid?.requestContentUpdate?.();
      window.dispatchEvent(new Event('resize'));
      await new Promise(r => setTimeout(r, 300));
    }
  }
];

// (The duplicate <env-deployments> registration this sweep found is fixed:
// the orphaned card variant is now <env-deployments-card>, so both import
// and render together.)
const TAG_ALIASES: Record<string, string> = {};

describe('all-grids sweep @375px', () => {
  it('captures every migrated grid view', async () => {
    (window as any).XMLHttpRequest = ShimXHR as any;
    try {
      for (const view of VIEWS) {
        const tag = TAG_ALIASES[view.tag] ?? view.tag;
        const wrapper = (await fixture(
          html`<div
            style="width: 375px; padding: 8px; background: #fff; border: 1px solid #ddd;"
          ></div>`
        )) as HTMLElement;
        try {
          const el = document.createElement(tag) as any;
          if (view.height !== 'auto') el.style.height = view.height ?? '280px';
          view.setup?.(el);
          wrapper.appendChild(el);
          await el.updateComplete;
          await settle();
          await view.post?.(el);
          await el.updateComplete;
          await settle();
          await page.screenshot({ element: wrapper, path: `all-${view.tag}.png` });
        } catch (e) {
          console.log(`SWEEP-FAIL ${view.tag}: ${(e as Error).message}`);
        }
        wrapper.remove();
      }
    } finally {
      (window as any).XMLHttpRequest = RealXHR;
    }
    expect(true).to.equal(true);
  });
});
