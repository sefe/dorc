/**
 * Screenshot gallery — visual record of the narrow list-row rendering vs the
 * wide table at several container widths. Not a test; run on demand like the
 * U-18 instrument (outside the CI glob).
 */
import { fixture, html, expect } from '../_helpers';
import { page } from '@vitest/browser/context';
// The app loads Lumo globally via /vaadin-theme/lumo.css — mirror that here.
// The dist build is flat (no @imports), so raw injection is deterministic.
import lumoFlat from '@vaadin/vaadin-lumo-styles/dist/lumo.css?raw';

const lumoStyle = document.createElement('style');
lumoStyle.textContent = lumoFlat;
document.head.appendChild(lumoStyle);
document.documentElement.style.fontFamily = 'var(--lumo-font-family)';
document.documentElement.style.fontSize = 'var(--lumo-font-size-m)';
document.documentElement.style.color = 'var(--lumo-body-text-color)';
import '../../src/components/component-deployment-results';
import '../../src/components/attached-app-users';
import '../../src/components/attached-servers';
import '../../src/components/env-deployments';

const settle = async (n = 3) => {
  for (let i = 0; i < n; i++)
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
};

const results = [
  {
    ComponentName: 'Dorc.Api.Deploy',
    Status: 'Failed',
    Log: 'System.Exception: deploy step failed at ApplyMigrations',
    StartedTime: '2026-08-02T14:32:07',
    CompletedTime: '2026-08-02T14:39:51',
    Id: 1
  },
  {
    ComponentName: 'Dorc.Web',
    Status: 'Complete',
    Log: 'OK',
    StartedTime: '2026-08-02T14:30:01',
    CompletedTime: '2026-08-02T14:31:22',
    Id: 2
  },
  {
    ComponentName: 'Dorc.Monitor',
    Status: 'WaitingConfirmation',
    Log: 'Plan ready',
    StartedTime: '2026-08-02T14:31:30',
    CompletedTime: null,
    Id: 3
  }
];

const users = [
  { DisplayName: 'Ben Hegarty', LoginId: 'ben.hegarty', LoginType: 'AD', LanId: 'SEFE\\ben.hegarty', LanIdType: 'Domain', Team: 'Platform' },
  { DisplayName: 'App Support', LoginId: 'app.support', LoginType: 'AD', LanId: 'SEFE\\app.support', LanIdType: 'Domain', Team: 'Support' }
];

const servers = [
  { Name: 'GMPR-DORAPP02', OsName: 'Windows Server 2022', ApplicationTags: 'dorc;prod', Id: 1 },
  { Name: 'GMPR-DORAPP03', OsName: 'Windows Server 2019', ApplicationTags: 'dorc', Id: 2 }
];

const builds = [
  { ComponentName: 'Dorc.Api.Deploy', RequestDetails: 'Trading-Platform 2026.08.02.3', UpdateDate: '02/08/2026 14:32', State: 'Complete' },
  { ComponentName: 'Dorc.Web', RequestDetails: 'Trading-Platform 2026.08.02.3', UpdateDate: '02/08/2026 14:31', State: 'Failed' }
];

async function shoot(
  name: string,
  width: number,
  tag: string,
  prop: string,
  items: unknown[],
  opts: { clickRow?: boolean } = {}
) {
  const wrapper = (await fixture(
    html`<div
      style="width: ${width}px; padding: 8px; background: #fff; border: 1px solid #ddd;"
    ></div>`
  )) as HTMLElement;
  const el = document.createElement(tag) as HTMLElement & {
    updateComplete: Promise<unknown>;
  } & Record<string, unknown>;
  el[prop] = items;
  el.style.height = '280px';
  wrapper.appendChild(el);
  await el.updateComplete;
  await settle();
  if (opts.clickRow) {
    const cell = el.shadowRoot!.querySelector(
      'vaadin-grid-cell-content .dorc-list-row'
    ) as HTMLElement | null;
    cell?.click();
    await settle();
  }
  await settle(6);
  await page.screenshot({ element: wrapper, path: `${name}.png` });
  wrapper.remove();
}

describe('screenshot gallery', () => {
  it('captures the gallery', async () => {
    // The on-call triage panel: deployment component results
    await shoot('deployment-results-375-phone', 375, 'component-deployment-results', 'resultItems', results);
    await shoot('deployment-results-375-phone-details-open', 375, 'component-deployment-results', 'resultItems', results, { clickRow: true });
    await shoot('deployment-results-600-tablet', 600, 'component-deployment-results', 'resultItems', results);
    await shoot('deployment-results-1100-desktop', 1100, 'component-deployment-results', 'resultItems', results);

    // Batch-migrated views
    await shoot('app-users-375-phone', 375, 'attached-app-users', 'users', users);
    await shoot('app-users-1100-desktop', 1100, 'attached-app-users', 'users', users);
    await shoot('attached-servers-375-phone', 375, 'attached-servers', 'servers', servers);
    await shoot('env-deployments-375-phone', 375, 'env-deployments-card', 'builds', builds);
    await shoot('env-deployments-900-desktop', 900, 'env-deployments-card', 'builds', builds);
    expect(true).to.equal(true);
  });
});
