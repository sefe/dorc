import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { expect, fixture, html, settle } from '../_helpers';
import type { Checkbox } from '@vaadin/checkbox';
import type { NumberField } from '@vaadin/number-field';
import type { TextField } from '@vaadin/text-field';
import type {
  EnvironmentApiModel,
  EphemeralEnvironmentTeardownResponseApiModel
} from '../../src/apis/dorc-api';
import type { EphemeralEnvironmentBanner } from '../../src/components/ephemeral-environment-banner';

const mocks = vi.hoisted(() => ({
  extendTtl: vi.fn(),
  teardown: vi.fn(),
  getEnvironments: vi.fn(),
  navigate: vi.fn()
}));

vi.mock('../../src/router/router', () => ({ navigate: mocks.navigate }));
vi.mock('../../src/apis/dorc-api', async importOriginal => ({
  ...(await importOriginal<Record<string, unknown>>()),
  EphemeralEnvironmentsApi: class {
    ephemeralEnvironmentsEnvironmentNameExtendTtlPost = mocks.extendTtl;
    ephemeralEnvironmentsEnvironmentNameTeardownPost = mocks.teardown;
  },
  RefDataEnvironmentsApi: class {
    refDataEnvironmentsGet = mocks.getEnvironments;
  }
}));

await import('../../src/components/ephemeral-environment-banner');

function findButton(root: ParentNode, textMatch: string): HTMLElement {
  const found = Array.from(root.querySelectorAll('vaadin-button')).find(b =>
    b.textContent?.trim().includes(textMatch)
  );
  expect(found, `button containing "${textMatch}"`).not.to.equal(undefined);
  return found as HTMLElement;
}

describe('EphemeralEnvironmentBanner', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getEnvironments.mockReturnValue(of([]));
  });

  it('renders nothing when environment is undefined or IsEphemeral is false', async () => {
    const emptyEl = await fixture<EphemeralEnvironmentBanner>(html`
      <ephemeral-environment-banner></ephemeral-environment-banner>
    `);
    expect(emptyEl.shadowRoot!.querySelector('.banner')).to.equal(null);

    const nonEphEl = await fixture<EphemeralEnvironmentBanner>(html`
      <ephemeral-environment-banner
        .environment=${{
          EnvironmentId: 1,
          EnvironmentName: 'non-eph',
          IsEphemeral: false
        }}
      ></ephemeral-environment-banner>
    `);
    expect(nonEphEl.shadowRoot!.querySelector('.banner')).to.equal(null);
  });

  it('renders tier pills, lifecycle states, and remaining TTL countdown across tiers and states', async () => {
    const futureTwoHours = new Date(
      Date.now() + 2 * 3600 * 1000 + 5 * 60 * 1000
    ).toISOString();

    const env: EnvironmentApiModel = {
      EnvironmentId: 10,
      EnvironmentName: 'traveler-ephemeral-pr-42',
      IsEphemeral: true,
      EnvironmentTier: 'ephemeral',
      EphemeralState: 'Ready',
      ExpiresAtUtc: futureTwoHours,
      UserEditable: true
    };

    const el = await fixture<EphemeralEnvironmentBanner>(html`
      <ephemeral-environment-banner
        full-width
        .environment=${env}
      ></ephemeral-environment-banner>
    `);

    const banner = el.shadowRoot!.querySelector('.banner')!;
    expect(banner).not.to.equal(null);
    expect(banner.textContent).to.contain('EPHEMERAL');
    expect(banner.textContent).to.contain('ephemeral');
    expect(banner.textContent).to.contain('Ready');
    expect(banner.textContent).to.contain('Expires in 2h');

    const tierPill = el.shadowRoot!.querySelector('.pill-tier')!;
    expect(tierPill.classList.contains('tier-shared')).to.equal(true);

    // Switch through tiers: dv, qa, ut, pp, custom
    for (const [tier, expectedClass] of [
      ['dv', 'tier-shared'],
      ['qa', 'tier-dedicated'],
      ['ut', 'tier-dedicated'],
      ['pp', 'tier-perf'],
      ['custom-tier', '']
    ] as const) {
      el.environment = { ...env, EnvironmentTier: tier };
      await el.updateComplete;
      const pill = el.shadowRoot!.querySelector('.pill-tier')!;
      if (expectedClass) {
        expect(pill.classList.contains(expectedClass)).to.equal(true);
      } else {
        expect(pill.textContent?.trim()).to.equal('custom-tier');
      }
    }

    // Switch through lifecycle states
    for (const [stateVal, expectedClass] of [
      ['Provisioning', 'pill-state-provisioning'],
      ['Testing', 'pill-state-testing'],
      ['TearingDown', 'pill-state-tearingdown'],
      ['TornDown', 'pill-state-torndown'],
      ['TeardownFailed', 'pill-state-teardownfailed'],
      ['ProvisionFailed', 'pill-state-provisionfailed'],
      ['ValidationFailed', 'pill-state-validationfailed'],
      ['CustomState', 'pill-state-ready']
    ] as const) {
      el.environment = { ...env, EphemeralState: stateVal };
      await el.updateComplete;
      expect(el.shadowRoot!.querySelector(`.${expectedClass}`)).not.to.equal(
        null
      );
    }
  });

  it('calculates warning, expired, no-expiry, and Date-instance countdown states accurately', async () => {
    const soonIso = new Date(Date.now() + 10 * 60 * 1000)
      .toISOString()
      .replace('Z', '');
    const el = await fixture<EphemeralEnvironmentBanner>(html`
      <ephemeral-environment-banner
        .environment=${{
          EnvironmentId: 11,
          EnvironmentName: 'eph-warn',
          IsEphemeral: true,
          EphemeralState: 'Ready',
          ExpiresAtUtc: soonIso
        }}
      ></ephemeral-environment-banner>
    `);

    let banner = el.shadowRoot!.querySelector('.banner')!;
    expect(banner.classList.contains('banner-warning')).to.equal(true);
    expect(
      el
        .shadowRoot!.querySelector('.pill-countdown')!
        .classList.contains('countdown-warning')
    ).to.equal(true);

    // Expired timestamp
    el.environment = {
      EnvironmentId: 11,
      EnvironmentName: 'eph-warn',
      IsEphemeral: true,
      EphemeralState: 'Ready',
      ExpiresAtUtc: new Date(Date.now() - 60000).toISOString()
    };
    await el.updateComplete;
    banner = el.shadowRoot!.querySelector('.banner')!;
    expect(banner.classList.contains('banner-danger')).to.equal(true);
    expect(
      el.shadowRoot!.querySelector('.pill-countdown')!.textContent
    ).to.contain('Expired');

    // No expiry set in failed state vs normal state, and invalid date string
    el.environment = {
      EnvironmentId: 11,
      EnvironmentName: 'eph-warn',
      IsEphemeral: true,
      EphemeralState: 'ProvisionFailed',
      ExpiresAtUtc: '   '
    };
    await el.updateComplete;
    expect(
      el.shadowRoot!.querySelector('.pill-countdown')!.textContent
    ).to.contain('No Expiry Set');
    expect(
      el
        .shadowRoot!.querySelector('.banner')!
        .classList.contains('banner-danger')
    ).to.equal(true);

    // Date object instance & compact mode
    el.compact = true;
    el.showActionsInCompact = true;
    el.environment = {
      EnvironmentId: 11,
      EnvironmentName: 'eph-warn',
      IsEphemeral: true,
      EphemeralState: 'Ready',
      ExpiresAtUtc: new Date(Date.now() + 15 * 60 * 1000) as unknown as string
    };
    await el.updateComplete;
    expect(el.shadowRoot!.querySelector('.compact-strip')).not.to.equal(null);
    expect(el.shadowRoot!.querySelector('.actions-group')).not.to.equal(null);

    // Invalid Date instance and timezone offset string (+01:00) and invalid string
    const parseUtcMs = (
      el as unknown as { parseUtcMs: (v: unknown) => number | null }
    ).parseUtcMs.bind(el);
    expect(parseUtcMs(null)).to.equal(null);
    expect(parseUtcMs(new Date('invalid-date'))).to.equal(null);
    expect(parseUtcMs('not-a-valid-date')).to.equal(null);
    expect(parseUtcMs('2026-11-14T12:00:00+01:00')).to.be.a('number');

    // Guard branches for submitExtendTtl, submitTeardown, openDestroyRequestMonitor, handleDeletedEnvironmentOnPoll
    const internal = el as unknown as {
      submitExtendTtl: (m: number) => void;
      submitTeardown: () => void;
      openDestroyRequestMonitor: (e: Event, id?: number) => void;
      handleDeletedEnvironmentOnPoll: () => void;
      pollEnvironmentState: () => void;
    };
    internal.submitExtendTtl(0);
    internal.openDestroyRequestMonitor(new Event('click'), undefined);
    el.environment = undefined;
    internal.handleDeletedEnvironmentOnPoll();
    internal.pollEnvironmentState();
    internal.submitTeardown();
  });

  it('disables Extend TTL and Tear Down buttons when user lacks edit permissions', async () => {
    const el = await fixture<EphemeralEnvironmentBanner>(html`
      <ephemeral-environment-banner
        .environment=${{
          EnvironmentId: 12,
          EnvironmentName: 'eph-readonly',
          IsEphemeral: true,
          EphemeralState: 'Ready',
          UserEditable: false,
          IsOwner: false
        }}
      ></ephemeral-environment-banner>
    `);

    const extendBtn = findButton(
      el.shadowRoot!,
      'Extend TTL'
    ) as HTMLButtonElement;
    const teardownBtn = findButton(
      el.shadowRoot!,
      'Tear Down Now'
    ) as HTMLButtonElement;
    expect(extendBtn.disabled).to.equal(true);
    expect(teardownBtn.disabled).to.equal(true);

    // Owner can modify even if UserEditable is false
    el.environment = {
      ...el.environment!,
      IsOwner: true
    };
    await el.updateComplete;
    expect(extendBtn.disabled).to.equal(false);
    expect(teardownBtn.disabled).to.equal(false);
  });

  it('opens Extend TTL dialog, extends TTL via preset or custom value, and handles API errors', async () => {
    const initialEnv: EnvironmentApiModel = {
      EnvironmentId: 15,
      EnvironmentName: 'eph-extend-test',
      IsEphemeral: true,
      EphemeralState: 'Ready',
      ExpiresAtUtc: new Date(Date.now() + 3600 * 1000).toISOString(),
      UserEditable: true
    };

    const updatedEnv: EnvironmentApiModel = {
      ...initialEnv,
      ExpiresAtUtc: new Date(Date.now() + 3 * 3600 * 1000).toISOString()
    };
    mocks.extendTtl.mockReturnValueOnce(of(updatedEnv));

    const el = await fixture<EphemeralEnvironmentBanner>(html`
      <ephemeral-environment-banner
        .environment=${initialEnv}
      ></ephemeral-environment-banner>
    `);

    const updatedEvents: CustomEvent[] = [];
    el.addEventListener('ephemeral-environment-updated', e =>
      updatedEvents.push(e as CustomEvent)
    );

    findButton(el.shadowRoot!, 'Extend TTL').click();
    await settle();

    const dialogs = el.shadowRoot!.querySelectorAll('vaadin-dialog');
    const extendDialog = dialogs[0] as HTMLElement;
    const numField = extendDialog.querySelector(
      'vaadin-number-field'
    ) as NumberField;
    expect(numField).not.to.equal(null);

    numField.value = '90';
    numField.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: '90' } })
    );
    await el.updateComplete;

    findButton(extendDialog, 'Extend by 90m').click();
    await settle();

    expect(mocks.extendTtl.mock.calls[0][0]).to.deep.equal({
      environmentName: 'eph-extend-test',
      ephemeralEnvironmentExtendTtlRequestApiModel: {
        AdditionalMinutes: 90
      }
    });
    expect(updatedEvents.length).to.equal(1);

    // Error branch on preset button click
    mocks.extendTtl.mockReturnValueOnce(
      throwError(() => ({ response: { ExceptionMessage: 'Max TTL exceeded' } }))
    );
    findButton(el.shadowRoot!, 'Extend TTL').click();
    await settle();
    findButton(extendDialog, '+30m').click();
    await settle();
    expect(el.shadowRoot!.querySelector('error-notification')).not.to.equal(
      null
    );

    // Cancel button closes dialog
    findButton(extendDialog, 'Cancel').click();
    await settle();
  });

  it('opens Tear Down dialog, submits teardown, renders Destroy Request monitor link, and handles errors', async () => {
    const initialEnv: EnvironmentApiModel = {
      EnvironmentId: 16,
      EnvironmentName: 'eph-teardown-test',
      IsEphemeral: true,
      EphemeralState: 'TeardownFailed',
      UserEditable: true
    };

    const teardownResponse: EphemeralEnvironmentTeardownResponseApiModel = {
      EnvironmentName: 'eph-teardown-test',
      EphemeralState: 'TearingDown',
      DestroyRequestId: 501,
      DestroyRequestIds: [501, 502],
      EnvironmentDeleted: false
    };
    mocks.teardown.mockReturnValueOnce(of(teardownResponse));

    const el = await fixture<EphemeralEnvironmentBanner>(html`
      <ephemeral-environment-banner
        .environment=${initialEnv}
      ></ephemeral-environment-banner>
    `);

    const teardownEvents: CustomEvent[] = [];
    el.addEventListener('ephemeral-environment-teardown', e =>
      teardownEvents.push(e as CustomEvent)
    );

    // Button says Retry Teardown when in TeardownFailed state
    findButton(el.shadowRoot!, 'Retry Teardown').click();
    await settle();

    const dialogs = el.shadowRoot!.querySelectorAll('vaadin-dialog');
    const teardownDialog = dialogs[1] as HTMLElement;

    const reasonField = teardownDialog.querySelector(
      'vaadin-text-field'
    ) as TextField;
    reasonField.value = 'Done testing PR #977';
    reasonField.dispatchEvent(
      new CustomEvent('value-changed', {
        detail: { value: 'Done testing PR #977' }
      })
    );

    const deleteCheckbox = teardownDialog.querySelector(
      'vaadin-checkbox'
    ) as Checkbox;
    deleteCheckbox.checked = false;
    deleteCheckbox.dispatchEvent(
      new CustomEvent('checked-changed', { detail: { value: false } })
    );
    await el.updateComplete;

    findButton(teardownDialog, 'Retry Teardown Now').click();
    await settle();

    expect(mocks.teardown.mock.calls[0][0]).to.deep.equal({
      environmentName: 'eph-teardown-test',
      ephemeralEnvironmentTeardownRequestApiModel: {
        Reason: 'Done testing PR #977',
        ForceDeleteMetadataOnNoComponents: false,
        RetainMetadataAfterTeardown: true
      }
    });
    expect(teardownEvents.length).to.equal(1);

    // Destroy Request #501 and #502 buttons are rendered and navigate on click
    const reqBtn = findButton(el.shadowRoot!, 'Destroy Request #501');
    reqBtn.click();
    expect(mocks.navigate.mock.calls[0][0]).to.equal('/monitor-result/501');

    // Error branch on teardown
    el.environment = {
      ...initialEnv,
      EphemeralState: 'Ready'
    };
    await el.updateComplete;
    mocks.teardown.mockReturnValueOnce(
      throwError(() => new Error('Teardown API error'))
    );
    findButton(el.shadowRoot!, 'Tear Down Now').click();
    await settle();
    findButton(teardownDialog, 'Confirm Tear Down').click();
    await settle();
    expect(el.shadowRoot!.querySelector('error-notification')).not.to.equal(
      null
    );

    // Teardown Cancel button and teardown response without DestroyRequestId
    findButton(teardownDialog, 'Cancel').click();
    await settle();
    mocks.teardown.mockReturnValueOnce(
      of({
        EnvironmentId: 16,
        EnvironmentName: 'eph-teardown-test',
        EphemeralState: 'TearingDown',
        EnvironmentDeleted: false
      })
    );
    findButton(el.shadowRoot!, 'Tear Down Now').click();
    await settle();
    findButton(teardownDialog, 'Confirm Tear Down').click();
    await settle();
    expect(el.environment?.EphemeralState).to.equal('TearingDown');

    el.environment = {
      ...initialEnv,
      EphemeralState: 'Ready'
    };
    await el.updateComplete;
    mocks.teardown.mockReturnValueOnce(
      of({
        EnvironmentId: 16,
        EnvironmentName: 'eph-teardown-test',
        EphemeralState: 'TornDown',
        EnvironmentDeleted: true
      })
    );
    findButton(el.shadowRoot!, 'Tear Down Now').click();
    await settle();
    findButton(teardownDialog, 'Confirm Tear Down').click();
    await settle();
    expect(el.environment?.EphemeralState).to.equal('TornDown');
  });

  it('polls environment state and handles updated, missing, or 404 deleted environments', async () => {
    const initialEnv: EnvironmentApiModel = {
      EnvironmentId: 20,
      EnvironmentName: 'eph-poll-env',
      IsEphemeral: true,
      EphemeralState: 'Provisioning',
      UserEditable: true
    };

    const el = await fixture<EphemeralEnvironmentBanner>(html`
      <ephemeral-environment-banner
        .environment=${initialEnv}
      ></ephemeral-environment-banner>
    `);

    const events: CustomEvent[] = [];
    el.addEventListener('ephemeral-environment-updated', e =>
      events.push(e as CustomEvent)
    );

    // 1. Poll returns updated Ready state
    mocks.getEnvironments.mockReturnValueOnce(
      of([
        {
          EnvironmentId: 20,
          EnvironmentName: 'eph-poll-env',
          IsEphemeral: true,
          EphemeralState: 'Ready'
        }
      ])
    );
    (
      el as unknown as { pollEnvironmentState: () => void }
    ).pollEnvironmentState();
    await settle(20);
    expect(el.environment?.EphemeralState).to.equal('Ready');
    expect(events.length).to.equal(1);

    // 2. Poll returns empty array or a replacement incarnation with a different EnvironmentId -> environment was deleted after teardown
    mocks.getEnvironments.mockReturnValueOnce(
      of([
        {
          EnvironmentId: 99,
          EnvironmentName: 'eph-poll-env',
          IsEphemeral: true,
          EphemeralState: 'Ready'
        }
      ])
    );
    (
      el as unknown as { pollEnvironmentState: () => void }
    ).pollEnvironmentState();
    await settle(20);
    expect(el.environment?.EphemeralState).to.equal('TornDown');
    expect(events[events.length - 1].detail.deleted).to.equal(true);

    // 2b. When EnvironmentId is undefined on the banner's current environment, polling matches by EnvironmentName
    el.environment = {
      EnvironmentName: 'eph-poll-env',
      IsEphemeral: true,
      EphemeralState: 'Provisioning'
    };
    await el.updateComplete;
    mocks.getEnvironments.mockReturnValueOnce(
      of([
        {
          EnvironmentId: 99,
          EnvironmentName: 'eph-poll-env',
          IsEphemeral: true,
          EphemeralState: 'Ready'
        }
      ])
    );
    (
      el as unknown as { pollEnvironmentState: () => void }
    ).pollEnvironmentState();
    await settle(20);
    expect(el.environment?.EnvironmentId).to.equal(99);

    // 2c. Switching to a different environment resets open dialogs and destroy request IDs
    (el as unknown as { extendDialogOpened: boolean }).extendDialogOpened =
      true;
    (el as unknown as { teardownDialogOpened: boolean }).teardownDialogOpened =
      true;
    (
      el as unknown as { lastDestroyRequestIds: number[] }
    ).lastDestroyRequestIds = [501];
    el.environment = {
      EnvironmentId: 200,
      EnvironmentName: 'eph-switched-env',
      IsEphemeral: true,
      EphemeralState: 'Ready'
    };
    await el.updateComplete;
    expect(
      (el as unknown as { extendDialogOpened: boolean }).extendDialogOpened
    ).to.equal(false);
    expect(
      (el as unknown as { teardownDialogOpened: boolean }).teardownDialogOpened
    ).to.equal(false);
    expect(
      (el as unknown as { lastDestroyRequestIds: number[] })
        .lastDestroyRequestIds.length
    ).to.equal(0);

    // 3. Poll returns 404 error -> transitions to TornDown
    el.environment = {
      ...initialEnv,
      EphemeralState: 'TearingDown'
    };
    await el.updateComplete;
    mocks.getEnvironments.mockReturnValueOnce(
      throwError(() => ({ status: 404 }))
    );
    (
      el as unknown as { pollEnvironmentState: () => void }
    ).pollEnvironmentState();
    await settle(20);
    expect(el.environment?.EphemeralState).to.equal('TornDown');

    // 4. Poll returns non-404 error -> does not transition to TornDown
    el.environment = {
      ...initialEnv,
      EphemeralState: 'Ready'
    };
    await el.updateComplete;
    mocks.getEnvironments.mockReturnValueOnce(
      throwError(() => ({ status: 500 }))
    );
    (
      el as unknown as { pollEnvironmentState: () => void }
    ).pollEnvironmentState();
    await settle(20);
    expect(el.environment?.EphemeralState).to.equal('Ready');
  });
});
