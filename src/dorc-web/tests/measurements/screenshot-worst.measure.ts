/**
 * Worst-case screenshot gallery — the five hardest views at phone width,
 * fed deliberately hostile data (long URLs, long version strings, long
 * comments, interactive cells). Companion to screenshot-gallery.measure.ts.
 */
import { fixture, html, expect } from '../_helpers';
import { page } from '@vitest/browser/context';
import lumoFlat from '@vaadin/vaadin-lumo-styles/dist/lumo.css?raw';

const lumoStyle = document.createElement('style');
lumoStyle.textContent = lumoFlat;
document.head.appendChild(lumoStyle);
document.documentElement.style.fontFamily = 'var(--lumo-font-family)';
document.documentElement.style.fontSize = 'var(--lumo-font-size-m)';
document.documentElement.style.color = 'var(--lumo-body-text-color)';

import '../../src/pages/page-env-history';
import '../../src/components/make-like-production';
import '../../src/components/add-edit-access-control';
import '../../src/components/component-previous-attempts';
import '../../src/pages/page-projects-list';

const settle = async (n = 6) => {
  for (let i = 0; i < n; i++)
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
};

async function frame(width: number): Promise<HTMLElement> {
  return (await fixture(
    html`<div
      style="width: ${width}px; padding: 8px; background: #fff; border: 1px solid #ddd;"
    ></div>`
  )) as HTMLElement;
}

async function snap(wrapper: HTMLElement, name: string) {
  await settle();
  await page.screenshot({ element: wrapper, path: `${name}.png` });
  wrapper.remove();
}

describe('worst-case gallery', () => {
  it('page-env-history — worst measured view (+389px), long comment, edit actions', async () => {
    const wrapper = await frame(375);
    const el = document.createElement('page-env-history') as HTMLElement & Record<string, any>;
    el.style.height = '340px';
    wrapper.appendChild(el);
    await el.updateComplete;
    el.setEnvHistory([
      {
        EnvName: 'PROD-EU-TRADING-01',
        UpdateDate: '02/08/2026 14:32:07',
        UpdatedBy: 'SEFE\\ben.hegarty',
        UpdateType: 'Component update',
        FromValue: 'Trading-Platform-2026.07.28.1-hotfix-rollback',
        ToValue: 'Trading-Platform-2026.08.02.3-release-candidate',
        Details: 'Deployed via pipeline after failed smoke test retry',
        Comment:
          'Routine release, but note the rollback of the hotfix from last week required re-applying the schema migration before this deploy could proceed cleanly.'
      },
      {
        EnvName: 'PROD-EU-TRADING-01',
        UpdateDate: '28/07/2026 09:11:45',
        UpdatedBy: 'SEFE\\app.support',
        UpdateType: 'Variable change',
        FromValue: 'ConnectionTimeout=30',
        ToValue: 'ConnectionTimeout=120',
        Details: 'Incident 4471 mitigation',
        Comment: 'Raised during on-call'
      }
    ]);
    await el.updateComplete;
    await settle();
    // open the first row's disclosure — the From/To version strings are the stress
    (el.shadowRoot!.querySelector('vaadin-grid-cell-content .dorc-list-row') as HTMLElement)?.click();
    await snap(wrapper, 'worst-env-history-375-details-open');
  });

  it('make-like-production — dialog-hosted, was +325px, long property values', async () => {
    const wrapper = await frame(340); // dialogs give less than the viewport
    const el = document.createElement('make-like-production') as HTMLElement & Record<string, any>;
    el.propertyOverrides = [
      { PropertyName: 'DeploymentTargetDatabaseConnectionString', PropertyValue: 'Server=GMPR-SQL01.sefe.local,1433;Database=DORC_PROD_01;Integrated Security=SSPI;TrustServerCertificate=true' },
      { PropertyName: 'RestoreSourceShare', PropertyValue: '\\\\gmpr-fs01.sefe.local\\backups\\trading\\weekly' }
    ];
    wrapper.appendChild(el);
    await el.updateComplete;
    await settle();
    const details = el.shadowRoot!.querySelector('vaadin-details') as HTMLElement & { opened: boolean };
    details.opened = true;
    await snap(wrapper, 'worst-make-like-production-340');
  });

  it('add-edit-access-control — interactive checkbox cells in the metadata line', async () => {
    const wrapper = await frame(375);
    const el = document.createElement('add-edit-access-control') as HTMLElement & Record<string, any>;
    el.style.minHeight = '300px';
    el.Privileges = [
      { Name: 'SEFE\\DORC-Prod-Administrators', Allow: 1, Deny: 0, Id: 1 },
      { Name: 'SEFE\\Trading-Platform-Developers', Allow: 2, Deny: 0, Id: 2 },
      { Name: 'SEFE\\app.support', Allow: 4, Deny: 0, Id: 3 }
    ];
    el.UserEditable = true;
    wrapper.appendChild(el);
    await el.updateComplete;
    await settle();
    // the whole form lives in a paper-dialog — open it and pin it into flow
    const dlg = el.shadowRoot!.querySelector('paper-dialog') as HTMLElement & { open: () => void };
    dlg.style.position = 'static';
    dlg.style.margin = '0';
    dlg.style.maxHeight = 'none';
    dlg.style.maxWidth = '100%';
    dlg.open();
    await settle();
    const acGrid = el.shadowRoot!.querySelector('vaadin-grid') as HTMLElement | null;
    acGrid?.scrollIntoView();
    await snap(wrapper, 'worst-access-control-375');
  });

  it('component-previous-attempts — every slot is a reused host renderer', async () => {
    const wrapper = await frame(375);
    const el = document.createElement('component-previous-attempts') as HTMLElement & Record<string, any>;
    el.style.minHeight = '280px';
    el.attemptItems = [
      {
        AttemptNumber: 2,
        Status: 'Failed',
        StartedTime: '2026-08-02T13:02:07',
        CompletedTime: '2026-08-02T13:04:51',
        UserName: 'SEFE\\ben.hegarty',
        Log: 'request log',
        ComponentResults: [
          {
            ComponentName: 'Dorc.Api.Deploy',
            Status: 'Failed',
            StartedTime: '2026-08-02T13:02:07',
            CompletedTime: '2026-08-02T13:04:51',
            Log: 'System.Data.SqlClient.SqlException: Timeout expired. The timeout period elapsed prior to completion of the operation.',
            Id: 11
          },
          {
            ComponentName: 'Dorc.Web',
            Status: 'Complete',
            StartedTime: '2026-08-02T13:00:00',
            CompletedTime: '2026-08-02T13:01:10',
            Log: 'OK',
            Id: 12
          }
        ]
      }
    ];
    wrapper.appendChild(el);
    await el.updateComplete;
    await settle();
    const attemptDetails = el.shadowRoot!.querySelector('vaadin-details') as HTMLElement & { opened: boolean };
    attemptDetails.opened = true;
    await snap(wrapper, 'worst-previous-attempts-375');
  });

  it('page-projects-list — long artefact URLs in disclosure', async () => {
    const wrapper = await frame(375);
    const el = document.createElement('page-projects-list') as HTMLElement & Record<string, any>;
    el.style.height = '340px';
    el.filteredProjects = [
      {
        ProjectName: 'Trading-Platform',
        ProjectDescription: 'Core trading platform services and scheduled jobs',
        ArtefactsUrl: 'https://tfs.sefe.local/tfs/DefaultCollection/Trading/_build?definitionId=1247&view=runs',
        ArtefactsSubPaths: 'drop/release;drop/scripts;drop/config',
        ArtefactsBuildRegex: '^Trading-Platform-\\d{4}\\.\\d{2}\\.\\d{2}\\.\\d+$',
        LeanIXUrl: 'https://sefe.leanix.net/FactSheet/Application/8f9a2b',
        Id: 1
      },
      {
        ProjectName: 'DORC',
        ProjectDescription: 'Deployment orchestrator',
        ArtefactsUrl: 'https://tfs.sefe.local/tfs/DefaultCollection/Platform/_build?definitionId=88',
        ArtefactsSubPaths: 'drop',
        ArtefactsBuildRegex: '.*',
        LeanIXUrl: '',
        Id: 2
      }
    ];
    const injected = el.filteredProjects;
    wrapper.appendChild(el);
    await el.updateComplete;
    await settle();
    // the page's constructor API fetch fails in the harness: clear its
    // loading gate and re-assert the injected data
    el.loading = false;
    el.projects = injected;
    el.filteredProjects = injected;
    el.requestUpdate();
    await el.updateComplete;
    await settle();
    (el.shadowRoot!.querySelector('.dorc-list-row__disclose') as HTMLElement)?.click();
    await snap(wrapper, 'worst-projects-list-375-details-open');
  });

  it('sanity', () => expect(true).to.equal(true));
});
