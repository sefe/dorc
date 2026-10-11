import { expect, fixture, html } from '../_helpers';
import { vi } from 'vitest';
import type { Tabs } from '@vaadin/tabs';
import '@vaadin/tabs';
import '@vaadin/vertical-layout';
import '../../src/pages/page-environment';
import '../../src/pages/page-environment-components';
import '../../src/components/environment-tabs/env-databases';
import '../../src/components/environment-tabs/env-servers';
import type { PageEnvironment } from '../../src/pages/page-environment';
import type { PageEnvironmentComponents } from '../../src/pages/page-environment-components';
import type { EnvDatabases } from '../../src/components/environment-tabs/env-databases';
import type { EnvServers } from '../../src/components/environment-tabs/env-servers';
import type { DatabaseAccessPanel } from '../../src/components/database-access-panel';
import { PageEnvBase } from '../../src/components/environment-tabs/page-env-base';
import { router } from '../../src/router/router';

describe('environment route updates', () => {
  it('updates the selected environment tab on a reused layout', async () => {
    const el = await fixture<PageEnvironment>(
      html`<page-environment></page-environment>`
    );

    el.onRouteUpdate({ pathname: '/environment/DEV1/variables' });

    const tabs = el.shadowRoot?.getElementById('env-tabs') as Tabs;
    expect(tabs.selected).to.equal(1);
  });

  describe('environment component layout', () => {
    beforeEach(() => {
      vi.spyOn(PageEnvBase.prototype, 'loadEnvironmentInfo').mockImplementation(
        () => {}
      );
      vi.spyOn(router, 'navigate').mockResolvedValue();
    });

    afterEach(() => vi.restoreAllMocks());

    it.each([360, 600])(
      'keeps the access panel reachable below a long database list at %ipx height',
      async height => {
        const frame = await fixture<HTMLDivElement>(html`
          <div style="height: ${height}px; width: 1000px; overflow: hidden;">
            <page-environment .environmentName=${'Test environment'}>
              <page-environment-components>
                <env-databases
                  .databases=${Array.from({ length: 20 }, (_, index) => ({
                    Id: index + 1,
                    Name: `Application_${index}`,
                    ServerName: 'SQL-SERVER'
                  }))}
                ></env-databases>
              </page-environment-components>
            </page-environment>
          </div>
        `);
        const environment =
          frame.querySelector<PageEnvironment>('page-environment')!;
        const components = frame.querySelector<PageEnvironmentComponents>(
          'page-environment-components'
        )!;
        const databases = frame.querySelector<EnvDatabases>('env-databases')!;
        await Promise.all([
          environment.updateComplete,
          components.updateComplete,
          databases.updateComplete
        ]);
        const details = databases.shadowRoot!.querySelector('vaadin-details')!;
        const access = databases.shadowRoot!.querySelector<DatabaseAccessPanel>(
          'database-access-panel'
        )!;
        await access.updateComplete;
        access.shadowRoot!.querySelector('vaadin-details')!.opened = true;

        await vi.waitFor(() => {
          expect(details.clientHeight).to.be.greaterThan(0);
          expect(details.scrollHeight).to.be.greaterThan(details.clientHeight);
          expect(details.getBoundingClientRect().bottom).to.be.at.most(
            frame.getBoundingClientRect().bottom + 1
          );
        });
        const tabs = components.shadowRoot!.querySelector('vaadin-tabs')!;
        const tabsTop = tabs.getBoundingClientRect().top;
        details.scrollTop = details.scrollHeight;
        await vi.waitFor(() => {
          expect(details.scrollTop).to.be.greaterThan(0);
          const loadButton = access.shadowRoot!.querySelector('vaadin-button')!;
          expect(loadButton.clientHeight).to.be.greaterThan(0);
          expect(loadButton.getBoundingClientRect().bottom).to.be.at.most(
            frame.getBoundingClientRect().bottom + 1
          );
          expect(loadButton.getBoundingClientRect().top).to.be.at.least(
            details.getBoundingClientRect().top
          );
          expect(tabs.getBoundingClientRect().top).to.equal(tabsTop);
        });
      }
    );

    it('keeps the servers grid within the space below the component tabs', async () => {
      const frame = await fixture<HTMLDivElement>(html`
        <div style="height: 360px; width: 1000px; overflow: hidden;">
          <page-environment .environmentName=${'Test environment'}>
            <page-environment-components>
              <env-servers
                .servers=${Array.from({ length: 20 }, (_, index) => ({
                  ServerId: index + 1,
                  Name: `SERVER_${index}`
                }))}
              ></env-servers>
            </page-environment-components>
          </page-environment>
        </div>
      `);
      const servers = frame.querySelector<EnvServers>('env-servers')!;
      await servers.updateComplete;
      await vi.waitFor(() => {
        const grid = servers
          .shadowRoot!.querySelector('attached-servers')!
          .shadowRoot!.querySelector('vaadin-grid')!;
        expect(grid.clientHeight).to.be.greaterThan(0);
        expect(grid.getBoundingClientRect().bottom).to.be.at.most(
          frame.getBoundingClientRect().bottom + 1
        );
      });
    });
  });

  it('updates the selected component tab on a reused layout', async () => {
    const el = await fixture<PageEnvironmentComponents>(
      html`<page-environment-components></page-environment-components>`
    );

    el.onRouteUpdate({
      pathname: '/environment/DEV1/components/databases'
    });

    const tabs = el.shadowRoot?.getElementById('component-tabs') as Tabs;
    expect(tabs.selected).to.equal(1);
  });
});
