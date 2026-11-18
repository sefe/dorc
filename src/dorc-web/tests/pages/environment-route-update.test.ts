import { expect, fixture, html } from '../_helpers';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import type { Tabs } from '@vaadin/tabs';
import '@vaadin/tabs';
import '@vaadin/vertical-layout';
import GlobalCache from '../../src/global-cache';
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

    it('scrolls the databases grid horizontally instead of clipping row controls', async () => {
      await import('../../src/components/attached-databases');
      const frame = await fixture<HTMLDivElement>(html`
        <div style="width: 480px;">
          <attached-databases
            .databases=${[{ Id: 7, Name: 'App', ServerName: 'SQL01' }]}
            .canManageDatabaseAccess=${true}
          ></attached-databases>
        </div>
      `);
      const attached = frame.querySelector('attached-databases')!;
      const grid = attached.shadowRoot!.querySelector('vaadin-grid')!;
      const scroller = grid.shadowRoot!.querySelector<HTMLElement>('#table')!;
      await vi.waitFor(() => {
        expect(scroller.scrollWidth).to.be.greaterThan(scroller.clientWidth);
        const controls = attached.shadowRoot!.querySelector(
          'database-env-controls'
        )!;
        const buttons = controls.shadowRoot!.querySelectorAll('vaadin-button');
        expect(buttons).to.have.length(4);
        const cell = controls.closest('vaadin-grid-cell-content')!;
        expect(buttons[3].getBoundingClientRect().right).to.be.at.most(
          cell.getBoundingClientRect().right + 1
        );
      });
    });

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
      ).to.equal(null);
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

      // Footer Close button closes dialog and clears databaseAccessId (falling back to plain view title)
      const closeButton = await vi.waitFor(() => {
        const btn = [
          ...databases.shadowRoot!.querySelectorAll('vaadin-button'),
          ...document.querySelectorAll('vaadin-button')
        ].find(b => b.textContent?.trim() === 'Close') as
          | HTMLElement
          | undefined;
        expect(btn).not.to.equal(undefined);
        return btn!;
      });
      closeButton.click();
      await databases.updateComplete;
      expect(dialog.opened).to.equal(false);
      expect(dialog.headerTitle).to.equal('Database permissions');

      // Audit button opens audit view; opened-changed(false) resets selection
      const auditButton = controls.shadowRoot!.querySelector(
        '[aria-label="Database access audit"]'
      ) as HTMLElement;
      auditButton.click();
      await databases.updateComplete;
      expect(dialog.opened).to.equal(true);
      expect(dialog.headerTitle).to.equal(
        'Database access audit - SQL01 / App'
      );
      dialog.dispatchEvent(
        new CustomEvent('opened-changed', { detail: { value: false } })
      );
      await databases.updateComplete;
      expect(dialog.opened).to.equal(false);

      // Missing database Id is ignored; omitted view defaults to 'all' and uses EnvironmentId / undefined databases fallbacks
      attached.dispatchEvent(
        new CustomEvent('manage-database-access', {
          detail: { database: { Name: 'NoId' } }
        })
      );
      await databases.updateComplete;
      expect(dialog.opened).to.equal(false);

      databases.environment = { EnvironmentId: 99, IsOwner: true } as any;
      databases.databases = undefined;
      await databases.updateComplete;
      attached.dispatchEvent(
        new CustomEvent('manage-database-access', {
          detail: { database }
        })
      );
      await databases.updateComplete;
      expect(dialog.opened).to.equal(true);
      expect(dialog.headerTitle).to.equal('Database Access');
    });

    it('enables database access management for GlobalCache Admin roles across cached and streamed states', async () => {
      const gc = GlobalCache.getInstance();
      const prevRoles = gc.userRoles;
      const prevResp = gc.allRolesResp;
      try {
        gc.userRoles = ['Admin'];
        const cachedAdmin = await fixture<EnvDatabases>(
          html`<env-databases></env-databases>`
        );
        expect(
          (
            cachedAdmin.shadowRoot!.querySelector(
              'attached-databases'
            ) as HTMLElement & { canManageDatabaseAccess: boolean }
          ).canManageDatabaseAccess
        ).to.equal(true);

        (gc as unknown as { userRoles: string[] | undefined }).userRoles =
          undefined;
        gc.allRolesResp = of(['Admin']);
        const streamedAdmin = await fixture<EnvDatabases>(
          html`<env-databases></env-databases>`
        );
        await streamedAdmin.updateComplete;
        expect(
          (
            streamedAdmin.shadowRoot!.querySelector(
              'attached-databases'
            ) as HTMLElement & { canManageDatabaseAccess: boolean }
          ).canManageDatabaseAccess
        ).to.equal(true);

        (gc as unknown as { userRoles: string[] | undefined }).userRoles =
          undefined;
        gc.allRolesResp = throwError(() => 'roles error');
        const errSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        const erroredRoles = await fixture<EnvDatabases>(
          html`<env-databases></env-databases>`
        );
        expect(errSpy.mock.calls).to.deep.equal([['roles error']]);
        expect(
          (
            erroredRoles.shadowRoot!.querySelector(
              'attached-databases'
            ) as HTMLElement & { canManageDatabaseAccess: boolean }
          ).canManageDatabaseAccess
        ).to.equal(false);
      } finally {
        gc.userRoles = prevRoles;
        gc.allRolesResp = prevResp;
      }
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
