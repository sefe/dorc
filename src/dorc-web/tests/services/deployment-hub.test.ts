import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { of } from 'rxjs';
import { fixture, html, settle } from '../_helpers';

const mockUserManagerEvents = {
  userLoaded: null as ((u: unknown) => void) | null,
  userUnloaded: null as (() => void) | null,
  accessTokenExpiring: null as (() => void) | null,
  accessTokenExpired: null as (() => void) | null
};

const mockUserManager = {
  settings: {
    authority: 'https://idp.sefe.test',
    client_id: 'dorc-web-ui',
    scope: 'openid profile dorc-api.manage'
  },
  events: {
    addUserLoaded: vi.fn((cb: (u: unknown) => void) => {
      mockUserManagerEvents.userLoaded = cb;
    }),
    addUserUnloaded: vi.fn((cb: () => void) => {
      mockUserManagerEvents.userUnloaded = cb;
    }),
    addAccessTokenExpiring: vi.fn((cb: () => void) => {
      mockUserManagerEvents.accessTokenExpiring = cb;
    }),
    addAccessTokenExpired: vi.fn((cb: () => void) => {
      mockUserManagerEvents.accessTokenExpired = cb;
    })
  },
  signinRedirect: vi.fn(() => Promise.resolve()),
  signinCallback: vi.fn(() =>
    Promise.resolve({ access_token: 'cb-token', expired: false })
  ),
  signinSilent: vi.fn(),
  signoutRedirect: vi.fn(() => Promise.resolve()),
  signoutCallback: vi.fn(() => Promise.resolve()),
  getUser: vi.fn(() => Promise.resolve(null))
};

vi.mock('oidc-client-ts', () => ({
  UserManager: class {
    settings = mockUserManager.settings;
    events = mockUserManager.events;
    signinRedirect = mockUserManager.signinRedirect;
    signinCallback = mockUserManager.signinCallback;
    signinSilent = mockUserManager.signinSilent;
    signoutRedirect = mockUserManager.signoutRedirect;
    signoutCallback = mockUserManager.signoutCallback;
    getUser = mockUserManager.getUser;
  }
}));

let capturedHubAccessTokenFactory: (() => string | Promise<string>) | undefined;
let capturedHubUrl = '';

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl(
      url: string,
      options?: { accessTokenFactory?: () => string | Promise<string> }
    ) {
      capturedHubUrl = url;
      capturedHubAccessTokenFactory = options?.accessTokenFactory;
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    build() {
      return { state: 'Disconnected' };
    }
  }
}));

const { OAuthService, OAUTH_SCHEME, oauthServiceContainer } =
  await import('../../src/services/Account/OAuthService');
const { appConfig } = await import('../../src/app-config');
const { DeploymentHub } =
  await import('../../src/services/ServerEvents/DeploymentHub');
await import('../../src/components/component-previous-attempts');
import type { ComponentPreviousAttempts } from '../../src/components/component-previous-attempts';

describe('DeploymentHub and OAuthService', () => {
  const originalAuthScheme = appConfig.authenticationScheme;
  const originalDorcApi = appConfig.dorcApi;

  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    capturedHubAccessTokenFactory = undefined;
    capturedHubUrl = '';
    (DeploymentHub as unknown as { hubConnection: unknown }).hubConnection =
      undefined;
  });

  afterEach(() => {
    appConfig.authenticationScheme = originalAuthScheme;
    appConfig.dorcApi = originalDorcApi;
  });

  it('deduplicates concurrent silent token renewals and updates signedInUser', async () => {
    let resolveSilent!: (value: unknown) => void;
    mockUserManager.signinSilent.mockImplementationOnce(
      () =>
        new Promise(resolve => {
          resolveSilent = resolve;
        })
    );

    const service = new OAuthService({
      authority: 'https://idp.sefe.test',
      client_id: 'dorc-web-ui',
      redirect_uri: 'https://dorc.sefe.test/signin-callback.html'
    });

    const p1 = service.renewSession();
    const p2 = service.renewSession();
    expect(p1).toBe(p2);
    expect(mockUserManager.signinSilent).toHaveBeenCalledTimes(1);

    const renewedUser = { access_token: 'fresh-jwt', expired: false };
    resolveSilent(renewedUser);
    const result = await p1;

    expect(result).toEqual(renewedUser);
    expect(service.signedInUser).toEqual(renewedUser);
  });

  it('falls back to interactive sign-in once when silent renewal fails on an expired token', async () => {
    mockUserManager.signinSilent.mockRejectedValueOnce(
      new Error('login_required')
    );

    const service = new OAuthService({
      authority: 'https://idp.sefe.test',
      client_id: 'dorc-web-ui',
      redirect_uri: 'https://dorc.sefe.test/signin-callback.html'
    });

    const recovered = await service.recoverExpiredSession();
    expect(recovered).toBeNull();
    expect(mockUserManager.signinRedirect).toHaveBeenCalledTimes(1);

    service.signIn();
    expect(mockUserManager.signinRedirect).toHaveBeenCalledTimes(1);
  });

  it('renews expired OAuth tokens inside DeploymentHub SignalR accessTokenFactory on reconnect', async () => {
    appConfig.authenticationScheme = OAUTH_SCHEME;
    appConfig.dorcApi = 'https://api.sefe.test';

    vi.spyOn(oauthServiceContainer, 'service', 'get').mockReturnValue({
      signedInUser: { access_token: 'expired-jwt', expired: true } as never,
      getUser: () =>
        of({ access_token: 'expired-jwt', expired: true } as never),
      recoverExpiredSession: vi.fn(() =>
        Promise.resolve({
          access_token: 'renewed-hub-jwt',
          expired: false
        } as never)
      )
    } as never);

    DeploymentHub.getConnection();
    expect(capturedHubUrl).toContain('/hubs/deployments');
    expect(capturedHubAccessTokenFactory).toBeTypeOf('function');

    const token = await capturedHubAccessTokenFactory!();
    expect(token).toBe('renewed-hub-jwt');
  });

  it('renders previous deployment attempts in descending order and opens retained Terraform plan dialog', async () => {
    const el = await fixture<ComponentPreviousAttempts>(
      html`<component-previous-attempts
        .requestId=${501}
        .attemptItems=${[
          {
            AttemptNumber: 1,
            Status: 'Failed',
            StartedTime: '2026-11-01T10:00:00Z',
            CompletedTime: '2026-11-01T10:02:00Z',
            UserName: 'operator@sefe.eu',
            Log: 'Attempt 1 failed log',
            ComponentResults: [
              {
                Id: 11,
                DeploymentResultId: 901,
                ComponentId: 4,
                ProjectId: 2,
                ComponentName: 'StorageAccountCatalog',
                Status: 'Complete',
                IsTerraform: true,
                HasTerraformPlan: true,
                SourceRevisionLabel: '1.0.0',
                DefinitionRevision: 'rev-1'
              }
            ]
          },
          {
            AttemptNumber: 2,
            Status: 'Completed',
            StartedTime: '2026-11-01T10:05:00Z',
            CompletedTime: '2026-11-01T10:07:00Z',
            UserName: 'operator@sefe.eu',
            ComponentResults: []
          }
        ]}
      ></component-previous-attempts>`
    );
    await settle();

    const details = Array.from(
      el.shadowRoot!.querySelectorAll('vaadin-details')
    );
    expect(details).toHaveLength(2);
    expect(details[0].getAttribute('summary')).toBe('Attempt # 2 - Completed');
    expect(details[1].getAttribute('summary')).toBe('Attempt # 1 - Failed');

    const logBtn = details[1].querySelector(
      'vaadin-button[title="View Request Log"]'
    ) as HTMLElement;
    expect(logBtn).toBeTruthy();
    logBtn.click();
    await settle();
    expect(el.dialogOpened).toBe(true);
    expect(el.selectedLog).toBe('Attempt 1 failed log');

    (details[1] as HTMLElement & { opened: boolean }).opened = true;
    await settle();
    await new Promise(resolve => setTimeout(resolve, 50));

    const viewPlanBtn = Array.from(
      el.shadowRoot!.querySelectorAll('vaadin-button')
    ).find(b => b.textContent?.includes('View retained plan')) as HTMLElement;
    expect(viewPlanBtn).toBeTruthy();
    viewPlanBtn.click();
    await settle();

    const planDialog = el.shadowRoot!.querySelector(
      'terraform-plan-dialog'
    ) as HTMLElement & { deploymentResultId: number; opened: boolean };
    expect(planDialog).toBeTruthy();
    expect(planDialog.deploymentResultId).toBe(901);
    expect(planDialog.opened).toBe(true);
  });
});
