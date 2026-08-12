import { of } from 'rxjs';
import { vi } from 'vitest';
import { expect, fixture, html, settle } from '../_helpers';

const { requestPostSpy } = vi.hoisted(() => ({
  requestPostSpy: vi.fn(() => of({ Id: 73 }))
}));

vi.mock('../../src/apis/dorc-api', async importOriginal => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    PropertiesApi: class {
      propertiesGet = () => of([]);
    },
    RequestApi: class {
      requestPost = requestPostSpy;
      requestBuildDefinitionsGet = () => of([]);
      requestComponentsGet = () => of([]);
    }
  };
});

await import('../../src/components/deploy/notifications/successful-deploy-notification');
await import('../../src/components/deploy/deploy-env');

type Toast = HTMLElement & {
  open(): void;
};

const notificationCard = (toast: HTMLElement) => {
  const notification = toast.shadowRoot?.querySelector(
    'vaadin-notification'
  ) as (HTMLElement & { opened: boolean }) | null;
  const card = document.querySelector(
    'vaadin-notification-container vaadin-notification-card'
  ) as HTMLElement | null;
  return { notification, card };
};

describe('successful deployment notification', () => {
  it('dispatches monitor details and lets the user dismiss the persistent toast', async () => {
    const host = await fixture(html`<div></div>`);
    const toast = document.createElement(
      'successful-deploy-notification'
    ) as Toast;
    toast.setAttribute('envName', 'DEV');
    toast.setAttribute('selectedBuild', '2026.08.12 [PINNED]');
    toast.setAttribute('requestedDeploymentId', '73');
    host.appendChild(toast);
    await settle();
    toast.open();
    await settle();

    const { notification, card } = notificationCard(toast);
    expect(card, 'Vaadin renders a notification card').to.not.equal(null);
    expect(card?.textContent).to.contain('Started deployment request with ID:');
    expect(card?.textContent).to.contain('73');

    let opened: CustomEvent | undefined;
    toast.addEventListener('open-monitor-result', event => {
      opened = event as CustomEvent;
    });
    (
      card?.querySelector('vaadin-button[theme="primary"]') as HTMLElement
    ).click();

    expect(opened?.detail).to.deep.equal({
      request: {
        Id: 73,
        BuildNumber: '2026.08.12',
        EnvironmentName: 'DEV'
      },
      message: 'Show results for Request'
    });

    (
      card?.querySelector('vaadin-button[aria-label="Close"]') as HTMLElement
    ).click();
    await settle();
    expect(
      notification?.opened,
      'the close control dismisses the toast'
    ).to.equal(false);
  });
});

describe('deployment confirmation', () => {
  beforeEach(() => requestPostSpy.mockClear());

  const prepareDeployment = async () => {
    const el = await fixture<HTMLElement>(html`<deploy-env></deploy-env>`);
    const deploy = el as HTMLElement & {
      envName: string;
      selectedBuildId: string;
      selectedBuild: string;
      project: unknown;
      req: unknown;
      dialogOpened: boolean;
      updateComplete: Promise<unknown>;
    };
    await deploy.updateComplete;
    await settle();

    // The user-facing Deploy button reads the selected values and tree state.
    // Set only the same public inputs its surrounding page supplies.
    deploy.project = {
      ProjectId: 5,
      ProjectName: 'Payments',
      ArtefactsUrl: 'https://artefacts.example/builds'
    };
    await deploy.updateComplete;
    deploy.envName = 'DEV';
    deploy.selectedBuildId = 'build-9';
    deploy.selectedBuild = '2026.08.12';
    (deploy as unknown as { buildDef: string }).buildDef = 'Release';
    (
      deploy.shadowRoot?.querySelector('hegs-tree') as HTMLElement & {
        getCheckedComponents(): Array<{ data: { name: string } }>;
      }
    ).getCheckedComponents = () => [{ data: { name: 'Database' } }];

    const deployButton = Array.from(
      deploy.shadowRoot?.querySelectorAll('vaadin-button') ?? []
    ).find(
      button =>
        button.textContent?.trim() === 'Deploy' &&
        button.closest('vaadin-confirm-dialog') === null
    ) as HTMLElement;
    deployButton.click();
    await deploy.updateComplete;
    await settle();

    const dialog = deploy.shadowRoot?.querySelector(
      'vaadin-confirm-dialog#dialog'
    ) as HTMLElement & { opened: boolean };
    return { deploy, dialog };
  };

  it('opens a cancellation-safe confirmation from the user selections', async () => {
    const { deploy, dialog } = await prepareDeployment();
    expect(dialog.opened, 'valid selections open a confirmation').to.equal(
      true
    );
    expect(deploy.req).to.deep.equal({
      requestDto: {
        Project: 'Payments',
        Environment: 'DEV',
        BuildUrl: 'build-9',
        BuildText: 'Release',
        BuildNum: '2026.08.12',
        RequestProperties: [],
        Components: ['Database']
      }
    });

    dialog.opened = false;
    await settle();
    expect(deploy.dialogOpened, 'cancelling closes the confirmation').to.equal(
      false
    );
    expect(requestPostSpy.mock.calls).to.have.length(0);
  });

  it('submits only after the confirmation event', async () => {
    const { deploy, dialog } = await prepareDeployment();
    dialog.dispatchEvent(new CustomEvent('confirm'));
    await deploy.updateComplete;
    await settle();

    expect(requestPostSpy.mock.calls).to.have.length(1);
    expect((requestPostSpy.mock.calls as unknown[][])[0][0]).to.deep.equal(
      deploy.req
    );
    const successful = deploy.shadowRoot?.querySelector(
      'successful-deploy-notification'
    ) as HTMLElement;
    expect(successful).to.not.equal(null);

    // A real confirm dialog closes after it emits `confirm`; our dispatched
    // test event only exercises the host listener, so finish that lifecycle
    // before fixture cleanup removes the dialog host.
    dialog.opened = false;
    await settle();
    (
      successful.shadowRoot?.querySelector(
        'vaadin-notification'
      ) as HTMLElement & {
        opened: boolean;
      }
    ).opened = false;
    await settle();
    successful.remove();
  });
});
