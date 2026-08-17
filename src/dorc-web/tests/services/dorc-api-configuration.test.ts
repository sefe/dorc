import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { AjaxConfig } from 'rxjs/ajax';

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

const { dorcApiConfiguration } = await import(
  '../../src/services/dorc-api-configuration'
);

describe('dorcApiConfiguration', () => {
  beforeEach(() => {
    signInSpy.mockClear();
    signedInUserHolder.value = null;
    appConfigStub.authenticationScheme = 'NotSet';
    appConfigStub.dorcApi = 'https://api.example.test';
  });

  // Regression guard: the hand-edited runtime.ts this configuration replaced
  // set withCredentials on every request. The generated runtime does not, and
  // losing it silently breaks every call on a Windows-authenticated
  // deployment, where the API is cross-origin and the browser will not send
  // Negotiate credentials without it.
  it('sends credentials on every request', () => {
    const middleware = dorcApiConfiguration.middleware;
    expect(middleware.length).toBeGreaterThan(0);

    const request = middleware
      .filter(m => m.pre)
      .reduce<AjaxConfig>(
        (acc, m) => m.pre!(acc),
        { url: '/RefDataEnvironments', method: 'GET' } as AjaxConfig
      );

    expect(request.withCredentials).to.equal(true);
  });

  it('preserves the rest of the request when adding credentials', () => {
    const pre = dorcApiConfiguration.middleware[0].pre!;
    const result = pre({
      url: '/RefDataProjects',
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '{"a":1}'
    } as AjaxConfig);

    expect(result.url).to.equal('/RefDataProjects');
    expect(result.method).to.equal('POST');
    expect(result.body).to.equal('{"a":1}');
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
