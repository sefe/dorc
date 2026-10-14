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

    it('offers database access management from each database row only to owners and admins', async () => {
      const database = { Id: 7, Name: 'App', ServerName: 'SQL01' };
      const databases = await fixture<EnvDatabases>(
        html`<env-databases .databases=${[database]}></env-databases>`
      );
      const attached = databases.shadowRoot!.querySelector(
        'attached-databases'
      ) as HTMLElement & { canManageDatabaseAccess: boolean };
      expect(attached.canManageDatabaseAccess).to.equal(false);
      expect(databases.shadowRoot!.querySelector('database-access-panel')).to.be
        .null;

      databases.environment = { IsOwner: true } as any;
      await databases.updateComplete;
      expect(attached.canManageDatabaseAccess).to.equal(true);
      expect(
        databases.shadowRoot!.querySelector('#manage-database-access')
      ).to.equal(null);

      await vi.waitFor(() => {
        const controls = attached.shadowRoot!.querySelector(
          'database-env-controls'
        ) as HTMLElement & {
          canManageDatabaseAccess: boolean;
          updateComplete: Promise<boolean>;
        };
        expect(controls?.canManageDatabaseAccess).to.equal(true);
      });
      const controls = attached.shadowRoot!.querySelector(
        'database-env-controls'
      ) as HTMLElement & { updateComplete: Promise<boolean> };
      await controls.updateComplete;
      const manageAccess = controls.shadowRoot!.querySelector(
        '[aria-label="Assign database permissions"]'
      ) as HTMLElement;
      expect(manageAccess).not.to.equal(null);
      expect(
        controls.shadowRoot!.querySelector(
          '[aria-label="Migrate database access"]'
        )
      ).not.to.equal(null);
      expect(
        controls.shadowRoot!.querySelector(
          '[aria-label="Database access audit"]'
        )
      ).not.to.equal(null);
      manageAccess.click();
      await databases.updateComplete;

      const dialog = databases.shadowRoot!.querySelector(
        '#database-access-dialog'
      ) as HTMLElement & { opened: boolean; headerTitle: string };
      expect(dialog.opened).to.equal(true);
      expect(dialog.headerTitle).to.equal('Database permissions - SQL01 / App');
    });

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
