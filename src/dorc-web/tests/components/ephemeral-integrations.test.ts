import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { expect, fixture, html, settle } from '../_helpers';
import type {
  DeploymentRequestApiModel,
  EnvironmentApiModel
} from '../../src/apis/dorc-api';
import type { EnvironmentCard } from '../../src/components/environment-card';
import type { RequestStatusCard } from '../../src/components/request-status-card';
import type { PageEnvironment } from '../../src/pages/page-environment';
import type { PageEnvironmentsList } from '../../src/pages/page-environments-list';
import type { PageProjectEnvs } from '../../src/pages/page-project-envs';
import { PageEnvBase } from '../../src/components/environment-tabs/page-env-base';

const mocks = vi.hoisted(() => ({
  getEnvironments: vi.fn(),
  getProjectByName: vi.fn(),
  getProjects: vi.fn(),
  getTemplates: vi.fn(),
  getProjectEnvironments: vi.fn(),
  navigate: vi.fn()
}));

vi.mock('../../src/router/router', async importOriginal => ({
  ...(await importOriginal<Record<string, unknown>>()),
  navigate: mocks.navigate
}));
vi.mock('../../src/apis/dorc-api', async importOriginal => ({
  ...(await importOriginal<Record<string, unknown>>()),
  RefDataEnvironmentsApi: class {
    refDataEnvironmentsGet = mocks.getEnvironments;
  },
  RefDataProjectsApi: class {
    refDataProjectsProjectNameGet = mocks.getProjectByName;
    refDataProjectsGet = mocks.getProjects;
  },
  RefDataProjectEnvironmentMappingsApi: class {
    refDataProjectEnvironmentMappingsGet = mocks.getProjectEnvironments;
  },
  EphemeralEnvironmentsApi: class {
    ephemeralEnvironmentsTemplatesGet = mocks.getTemplates;
  }
}));

await import('../../src/components/environment-card');
await import('../../src/components/request-status-card');
await import('../../src/pages/page-environment');
await import('../../src/pages/page-environments-list');
await import('../../src/pages/page-project-envs');

class DummyEnvChildTab extends PageEnvBase {}
if (!customElements.get('dummy-env-child-tab')) {
  customElements.define('dummy-env-child-tab', DummyEnvChildTab);
}

describe('Ephemeral UI Integrations across Cards and Pages', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getEnvironments.mockReturnValue(of([]));
    mocks.getProjectByName.mockReturnValue(
      of({ ProjectId: 10, ProjectName: 'Traveler' })
    );
    mocks.getProjects.mockReturnValue(of([]));
    mocks.getTemplates.mockReturnValue(of([]));
    mocks.getProjectEnvironments.mockReturnValue(of({ Items: [] }));
  });

  it('EnvironmentCard renders compact ephemeral banner and propagates updates/deletions', async () => {
    const ephEnv: EnvironmentApiModel = {
      EnvironmentId: 40,
      EnvironmentName: 'traveler-eph-card',
      IsEphemeral: true,
      EnvironmentTier: 'ephemeral',
      EphemeralState: 'Ready',
      Details: {
        Description: 'Ephemeral card environment',
        EnvironmentOwner: 'tester'
      }
    };

    const card = await fixture<EnvironmentCard>(html`
      <environment-card .environment=${ephEnv}></environment-card>
    `);

    const banner = card.shadowRoot!.querySelector(
      'ephemeral-environment-banner'
    );
    expect(banner).not.to.equal(null);

    const envsChangedEvents: CustomEvent[] = [];
    card.addEventListener('envs-changed', e =>
      envsChangedEvents.push(e as CustomEvent)
    );

    banner!.dispatchEvent(
      new CustomEvent('ephemeral-environment-updated', {
        detail: {
          environment: { ...ephEnv, EphemeralState: 'TornDown' },
          deleted: true
        },
        bubbles: true,
        composed: true
      })
    );
    await card.updateComplete;

    expect(card.environment?.EphemeralState).to.equal('TornDown');
    expect(envsChangedEvents.length).to.equal(1);
    expect(envsChangedEvents[0].detail.deleted).to.equal(true);
  });

  it('RequestStatusCard renders EPHEMERAL, AUTO-APPROVE, and DESTROY badges and matches environment case-insensitively', async () => {
    const ephEnv: EnvironmentApiModel = {
      EnvironmentId: 50,
      EnvironmentName: 'traveler-eph-req',
      IsEphemeral: true,
      EnvironmentIsProd: false,
      EnvironmentTier: 'ephemeral',
      EphemeralState: 'TearingDown'
    };

    mocks.getEnvironments.mockReturnValue(
      of([
        {
          EnvironmentId: 49,
          EnvironmentName: 'other-env',
          IsEphemeral: false
        },
        ephEnv
      ])
    );

    const deployReq: DeploymentRequestApiModel = {
      Id: 777,
      Project: 'Traveler',
      EnvironmentName: 'traveler-eph-req',
      BuildNumber: '1.0.0',
      Status: 'Running',
      RequestDetails:
        '{"PropertyName":"TerraformOperation","PropertyValue":"Destroy"}'
    };

    const el = await fixture<RequestStatusCard>(html`
      <request-status-card
        .deployRequest=${deployReq}
        .selectedProject=${'Traveler'}
      ></request-status-card>
    `);
    await settle(50);
    await el.updateComplete;

    const badges = el.shadowRoot!.querySelector('.ephemeral-request-badges');
    expect(badges).not.to.equal(null);
    expect(badges!.textContent).to.contain('EPHEMERAL');
    expect(badges!.textContent).to.contain('AUTO-APPROVE');
    expect(badges!.textContent).to.contain('DESTROY');

    const banner = el.shadowRoot!.querySelector('ephemeral-environment-banner');
    expect(banner).not.to.equal(null);

    const refreshEvents: CustomEvent[] = [];
    el.addEventListener('refresh-monitor-result', e =>
      refreshEvents.push(e as CustomEvent)
    );
    banner!.dispatchEvent(
      new CustomEvent('ephemeral-environment-teardown', {
        detail: {
          environment: { ...ephEnv, EphemeralState: 'TornDown' }
        },
        bubbles: true,
        composed: true
      })
    );
    expect(refreshEvents.length).to.equal(1);

    const openDetailEvents: CustomEvent[] = [];
    el.addEventListener('open-env-detail', e =>
      openDetailEvents.push(e as CustomEvent)
    );
    el.openEnvironmentDetails();
    await settle(20);
    expect(openDetailEvents.length).to.equal(1);
    expect(openDetailEvents[0].detail.Environment.EnvironmentName).to.equal(
      'traveler-eph-req'
    );

    // Non-destroy request on a tearing-down ephemeral environment should not show DESTROY badge
    el.deployRequest = {
      ...deployReq,
      RequestDetails:
        '{"PropertyName":"EphemeralTargetState","PropertyValue":"Ready"}'
    };
    await el.updateComplete;
    const nonDestroyBadges = el.shadowRoot!.querySelector(
      '.ephemeral-request-badges'
    );
    expect(nonDestroyBadges!.textContent).to.contain('EPHEMERAL');
    expect(nonDestroyBadges!.textContent).not.to.contain('DESTROY');

    // Deleted ephemeral environment (404 / error on refDataEnvironmentsGet) still renders badges from RequestDetails
    mocks.getEnvironments.mockReturnValue(
      throwError(() => new Error('404 Not Found'))
    );
    el.deployRequest = {
      ...deployReq,
      EnvironmentName: 'deleted-eph-env',
      RequestDetails:
        '{"PropertyName":"TerraformOperation","PropertyValue":"Destroy"}'
    };
    await settle(30);
    await el.updateComplete;
    const deletedBadges = el.shadowRoot!.querySelector(
      '.ephemeral-request-badges'
    );
    expect(deletedBadges).not.to.equal(null);
    expect(deletedBadges!.textContent).to.contain('EPHEMERAL');
    expect(deletedBadges!.textContent).to.contain('DESTROY');

    // Historical request whose EphemeralEnvironmentId does not match a newly recreated environment with the same name does not bind live banner controls
    mocks.getEnvironments.mockReturnValue(
      of([
        {
          EnvironmentId: 99,
          EnvironmentName: 'recreated-eph-env',
          IsEphemeral: true,
          EnvironmentIsProd: false,
          EphemeralState: 'Ready'
        }
      ])
    );
    el.deployRequest = {
      ...deployReq,
      EnvironmentName: 'recreated-eph-env',
      RequestDetails:
        '<DeploymentRequestDetail><Properties><PropertyPair><Name>OtherEphemeralEnvironmentId</Name><Value>99</Value></PropertyPair><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>50</Value></PropertyPair><PropertyPair><Name>EphemeralTargetState</Name><Value>Ready</Value></PropertyPair></Properties></DeploymentRequestDetail>'
    };
    await settle(30);
    await el.updateComplete;
    expect(
      el.shadowRoot!.querySelector('ephemeral-environment-banner')
    ).to.equal(null);
    expect(
      el.shadowRoot!.querySelector('.ephemeral-request-badges')!.textContent
    ).to.contain('EPHEMERAL');

    // Non-ephemeral environment does not render ephemeral/destroy badges
    mocks.getEnvironments.mockReturnValue(
      of([
        {
          EnvironmentId: 52,
          EnvironmentName: 'permanent-env',
          IsEphemeral: false,
          EnvironmentIsProd: false
        }
      ])
    );
    el.deployRequest = {
      ...deployReq,
      EnvironmentName: 'permanent-env',
      RequestDetails:
        '{"PropertyName":"TerraformOperation","PropertyValue":"Destroy"}'
    };
    await settle(30);
    await el.updateComplete;
    expect(el.shadowRoot!.querySelector('.ephemeral-request-badges')).to.equal(
      null
    );

    // Switching back to ephemeral and then to a request without an environment clears ephemeralEnv and ignores stale banner events
    mocks.getEnvironments.mockReturnValue(of([ephEnv]));
    el.deployRequest = deployReq;
    await settle(20);
    await el.updateComplete;

    // Ignore events from a mismatched environment
    (
      el as unknown as {
        onEphemeralEnvironmentUpdated: (e: CustomEvent) => void;
        onEphemeralEnvironmentTeardown: (e: CustomEvent) => void;
      }
    ).onEphemeralEnvironmentUpdated(
      new CustomEvent('ephemeral-environment-updated', {
        detail: {
          environment: {
            EnvironmentId: 999,
            EnvironmentName: 'mismatched-env'
          }
        }
      })
    );
    (
      el as unknown as {
        onEphemeralEnvironmentTeardown: (e: CustomEvent) => void;
      }
    ).onEphemeralEnvironmentTeardown(
      new CustomEvent('ephemeral-environment-teardown', {
        detail: {
          environment: {
            EnvironmentId: 999,
            EnvironmentName: 'mismatched-env'
          }
        }
      })
    );
    expect(
      (el as unknown as { ephemeralEnv?: EnvironmentApiModel }).ephemeralEnv
        ?.EnvironmentId
    ).to.equal(50);

    el.deployRequest = {
      ...deployReq,
      EnvironmentName: ''
    };
    await el.updateComplete;
    expect(
      (el as unknown as { ephemeralEnv?: EnvironmentApiModel }).ephemeralEnv
    ).to.equal(undefined);
  });

  it('PageEnvironment renders full-width ephemeral banner and propagates updates to slotted PageEnvBase children', async () => {
    const page = await fixture<PageEnvironment>(html`
      <page-environment>
        <dummy-env-child-tab></dummy-env-child-tab>
      </page-environment>
    `);

    const ephEnv: EnvironmentApiModel = {
      EnvironmentId: 60,
      EnvironmentName: 'traveler-eph-page',
      IsEphemeral: true,
      EnvironmentTier: 'qa',
      EphemeralState: 'Ready'
    };

    page.environmentLoaded(
      new CustomEvent('environment-loaded', {
        detail: { environment: ephEnv }
      })
    );
    await page.updateComplete;

    const banner = page.shadowRoot!.querySelector(
      'ephemeral-environment-banner'
    );
    expect(banner).not.to.equal(null);
    expect(banner!.hasAttribute('full-width')).to.equal(true);

    banner!.dispatchEvent(
      new CustomEvent('ephemeral-environment-updated', {
        detail: {
          environment: { ...ephEnv, EphemeralState: 'TearingDown' }
        },
        bubbles: true,
        composed: true
      })
    );
    await page.updateComplete;

    const childTab = page.querySelector(
      'dummy-env-child-tab'
    ) as DummyEnvChildTab;
    expect(childTab.environment?.EphemeralState).to.equal('TearingDown');

    // Navigating to another environment clears the previous environment banner immediately while loading or not found
    page.environmentLoading();
    await page.updateComplete;
    expect(page.environment).to.equal(undefined);
    expect(
      page.shadowRoot!.querySelector('ephemeral-environment-banner')
    ).to.equal(null);

    page.environmentLoaded(
      new CustomEvent('environment-loaded', {
        detail: { environment: ephEnv }
      })
    );
    await page.updateComplete;
    page.environmentNotFound();
    await page.updateComplete;
    expect(page.environment).to.equal(undefined);
  });

  it('PageEnvironmentsList and PageProjectEnvs handle ephemeral creation, state updates, and deletion', async () => {
    const envList = await fixture<PageEnvironmentsList>(html`
      <page-environments-list></page-environments-list>
    `);

    const ephEnv: EnvironmentApiModel = {
      EnvironmentId: 70,
      EnvironmentName: 'traveler-eph-list',
      IsEphemeral: true,
      EphemeralState: 'Provisioning'
    };

    envList.setEnvironments([
      {
        EnvironmentId: 1,
        EnvironmentName: 'Permanent-Dev',
        IsEphemeral: false
      }
    ]);
    envList.onEphemeralEnvironmentCreated(
      new CustomEvent('environment-created', {
        detail: { environment: ephEnv }
      })
    );
    expect(envList.environments?.length).to.equal(2);

    // Update state in list
    envList.handleEphemeralUpdated(
      new CustomEvent('ephemeral-environment-updated', {
        detail: {
          environment: { ...ephEnv, EphemeralState: 'Ready' },
          deleted: false
        }
      })
    );
    expect(
      envList.environments?.find(e => e.EnvironmentId === 70)?.EphemeralState
    ).to.equal('Ready');

    // Delete from list
    envList.handleEphemeralUpdated(
      new CustomEvent('ephemeral-environment-updated', {
        detail: {
          environment: ephEnv,
          deleted: true
        }
      })
    );
    expect(envList.environments?.length).to.equal(1);

    // PageProjectEnvs update handling and deletion refresh
    const projEnvs = await fixture<PageProjectEnvs>(html`
      <page-project-envs></page-project-envs>
    `);
    projEnvs.project = 'Traveler';
    projEnvs.openEphemeralWizard();
    envList.openEphemeralWizard();

    // Render row cell via _envNameRenderer for both ephemeral and non-ephemeral rows
    expect(envList._envNameRenderer(ephEnv)).not.to.equal(undefined);
    expect(
      envList._envNameRenderer({
        EnvironmentId: 2,
        EnvironmentName: 'NonEph',
        IsEphemeral: false
      })
    ).not.to.equal(undefined);
    envList.handleEphemeralUpdated(
      new CustomEvent('ephemeral-environment-updated', { detail: {} })
    );

    projEnvs.environments = [ephEnv];
    const projInternal = projEnvs as unknown as {
      handleEphemeralEnvironmentUpdated: (e: CustomEvent) => void;
    };
    projInternal.handleEphemeralEnvironmentUpdated(
      new CustomEvent('ephemeral-environment-updated', {
        detail: {
          environment: { ...ephEnv, EphemeralState: 'Ready' },
          deleted: false
        }
      })
    );
    expect(projEnvs.environments[0].EphemeralState).to.equal('Ready');

    projInternal.handleEphemeralEnvironmentUpdated(
      new CustomEvent('ephemeral-environment-updated', {
        detail: {
          environment: ephEnv,
          deleted: true
        }
      })
    );
    expect(mocks.getProjectEnvironments.mock.calls.length).to.be.greaterThan(0);
  });
});
