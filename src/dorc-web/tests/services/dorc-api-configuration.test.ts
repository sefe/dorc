import { describe, it, expect, vi, beforeEach } from 'vitest';

// Stand-ins for the two modules the configuration reads. Both are mutated per
// test to prove the configuration re-reads them rather than capturing values
// when the module is first loaded.
const appConfigStub = {
  dorcApi: 'https://api.example.test',
  authenticationScheme: 'NotSet'
};

const signedInUserHolder: { value: { access_token: string; expired: boolean } | null } = {
  value: null
};
const signInSpy = vi.fn();

vi.mock('../../src/app-config', () => ({
  appConfig: appConfigStub,
  default: class {}
}));

vi.mock('../../src/services/Account/OAuthService', () => ({
  OAUTH_SCHEME: 'OAuth',
  oauthServiceContainer: {
    get service() {
      return {
        get signedInUser() {
          return signedInUserHolder.value;
        },
        signIn: signInSpy
      };
    }
  }
}));

const { dorcApiConfiguration, resetSignInStateForTests } = await import(
  '../../src/services/dorc-api-configuration'
);
const { RefDataProjectsApi } = await import('../../src/apis/dorc-api');

describe('dorcApiConfiguration', () => {
  beforeEach(() => {
    signInSpy.mockClear();
    // Module-level state that outlives a single test: without this a test
    // that trips the latch silently changes the meaning of later ones.
    resetSignInStateForTests();
    signedInUserHolder.value = null;
    appConfigStub.authenticationScheme = 'NotSet';
    appConfigStub.dorcApi = 'https://api.example.test';
  });

  // Regression guard: the hand-edited runtime.ts this configuration replaced
  // set withCredentials on every request. The generated runtime does not, and
  // losing it silently breaks every call on a Windows-authenticated
  // deployment, where the API is cross-origin and the browser will not send
  // Negotiate credentials without it.
  //
  // This drives a real generated API and inspects the XMLHttpRequest it
  // produces, rather than asserting over the middleware array. Asserting the
  // array only proves this module's own contents: it would stay green if a
  // regenerated runtime stopped reading configuration.middleware at all, which
  // is precisely the silent failure being guarded against — and CI regenerates
  // that runtime on every build.
  it('sends credentials on a real request through a generated API', async () => {
    const originalOpen = XMLHttpRequest.prototype.open;
    const originalSend = XMLHttpRequest.prototype.send;

    // Resolve from the interceptor itself: the request is aborted rather than
    // allowed onto the network, so the observable never settles and awaiting
    // it would simply time out.
    const sent = new Promise<{ url: string; withCredentials: boolean }>(resolve => {
      XMLHttpRequest.prototype.open = function patchedOpen(
        this: XMLHttpRequest & { __url?: string },
        method: string,
        url: string
      ) {
        this.__url = url;
        return (originalOpen as unknown as (m: string, u: string) => void).call(
          this,
          method,
          url
        );
      } as typeof XMLHttpRequest.prototype.open;

      XMLHttpRequest.prototype.send = function patchedSend(
        this: XMLHttpRequest & { __url?: string }
      ) {
        resolve({ url: this.__url ?? '', withCredentials: this.withCredentials });
        this.abort();
      } as typeof XMLHttpRequest.prototype.send;
    });

    try {
      const api = new RefDataProjectsApi(dorcApiConfiguration);
      api.refDataProjectsGet().subscribe({ next: () => {}, error: () => {} });
      const observed = await sent;

      expect(observed.withCredentials).to.equal(true);
      expect(observed.url).to.contain('/RefDataProjects');
    } finally {
      XMLHttpRequest.prototype.open = originalOpen;
      XMLHttpRequest.prototype.send = originalSend;
    }
  });

  it('reads the base URL at request time, not at module load', () => {
    appConfigStub.dorcApi = 'https://changed.example.test';
    expect(dorcApiConfiguration.basePath).to.equal('https://changed.example.test');
  });

  it('supplies no bearer token when the scheme is not OAuth', () => {
    appConfigStub.authenticationScheme = 'WinAuth';
    signedInUserHolder.value = { access_token: 'tok', expired: false };

    expect(dorcApiConfiguration.accessToken).to.equal(undefined);
  });

  it('supplies a bearer token under OAuth', () => {
    appConfigStub.authenticationScheme = 'OAuth';
    signedInUserHolder.value = { access_token: 'tok', expired: false };

    expect(dorcApiConfiguration.accessToken!('oauth2')).to.equal('Bearer tok');
    expect(signInSpy).not.toHaveBeenCalled();
  });

  // An expiring token is renewed silently in the background; user.expired is
  // true for that window even though the session recovers on its own.
  it('still sends an expired token rather than interrupting silent renewal', () => {
    appConfigStub.authenticationScheme = 'OAuth';
    signedInUserHolder.value = { access_token: 'stale', expired: true };

    expect(dorcApiConfiguration.accessToken!('oauth2')).to.equal('Bearer stale');
    expect(signInSpy).not.toHaveBeenCalled();
  });

  it('starts sign-in once when there is no session, however many requests fire', () => {
    appConfigStub.authenticationScheme = 'OAuth';
    signedInUserHolder.value = null;

    const token = dorcApiConfiguration.accessToken!;
    token('oauth2');
    token('oauth2');
    token('oauth2');

    expect(signInSpy).toHaveBeenCalledTimes(1);
  });
});
