import type { AjaxConfig } from 'rxjs/ajax';
import { Configuration } from '../apis/dorc-api';
import { appConfig } from '../app-config';
import { OAUTH_SCHEME, oauthServiceContainer } from './Account/OAuthService';

// Starting sign-in does not stop the calling frame, so without this guard a
// page with N requests in flight starts N of them.
let signInRedirectStarted = false;

/** Test seam: the latch is module state and outlives an individual test. */
export function resetSignInStateForTests(): void {
  signInRedirectStarted = false;
  pendingSessionRecovery = null;
}

// If signIn() never navigates (identity provider unreachable) the latch holds
// for the life of the page and no login prompt appears. A timed lapse was
// tried and removed: location.assign() leaves the document running until the
// new one commits, so under the slow identity provider the lapse is meant to
// survive it fires mid-redirect and starts a second navigation that cancels
// the first. Reloading the page clears this state, which is the same recourse
// the user has for an unreachable identity provider anyway.
function redirectToSignIn(): void {
  if (signInRedirectStarted) {
    return;
  }
  signInRedirectStarted = true;
  // signIn() rather than location.assign('/signin.html'): it stores the
  // current URL under "lastUrl" so signInCallback can return the user to the
  // page they were on. /signin.html is a static page with a manual button and
  // signInCallback explicitly sends a "lastUrl" of signin.html back to "/",
  // so navigating there directly loses the user's place.
  oauthServiceContainer.service.signIn();
}

// Renewal in flight for the request currently being built, if any. The
// header getter runs before the XHR is created, so the XHR below can wait for
// it and send the renewed token rather than the expired one.
let pendingSessionRecovery: Promise<unknown> | null = null;

function bearer(token: string): string {
  return `Bearer ${token}`;
}

function oauthAuthorizationHeader(): string {
  const user = oauthServiceContainer.service.signedInUser;
  if (!user) {
    redirectToSignIn();
    return '';
  }
  if (user.expired && !pendingSessionRecovery) {
    const recovery = Promise.resolve(
      oauthServiceContainer.service.recoverExpiredSession?.(redirectToSignIn)
    ).finally(() => {
      if (pendingSessionRecovery === recovery) pendingSessionRecovery = null;
    });
    pendingSessionRecovery = recovery;
  }
  // The expired token is still returned so a request whose renewal fails is
  // rejected by the API in the usual way rather than sent unauthenticated.
  return bearer(user.access_token);
}

function currentBearerToken(fallback?: string): string | undefined {
  const token = oauthServiceContainer.service.signedInUser?.access_token;
  return token ? bearer(token) : fallback;
}

// The generated runtime builds the Authorization header synchronously, so a
// request made while the token is expired (a tab left open overnight) would
// go out with the stale token and 401 even though silent renewal succeeds a
// moment later. This XHR holds the Authorization header back and sends once
// any pending renewal has settled, with whatever token the session then has.
class SessionRenewingXhr extends XMLHttpRequest {
  private authorization?: string;
  private aborted = false;

  override setRequestHeader(name: string, value: string): void {
    if (name.toLowerCase() === 'authorization') {
      this.authorization = value;
      return;
    }
    super.setRequestHeader(name, value);
  }

  override abort(): void {
    this.aborted = true;
    super.abort();
  }

  override send(body?: Document | XMLHttpRequestBodyInit | null): void {
    const dispatch = () => {
      if (this.aborted) return;
      const token = currentBearerToken(this.authorization);
      if (token) super.setRequestHeader('Authorization', token);
      super.send(body);
    };
    const pending = pendingSessionRecovery;
    if (!pending) {
      dispatch();
      return;
    }
    pending.then(dispatch, dispatch);
  }
}

const sessionRenewingXhrMiddleware = {
  pre: (request: AjaxConfig): AjaxConfig =>
    appConfig.authenticationScheme === OAUTH_SCHEME
      ? { ...request, createXHR: () => new SessionRenewingXhr() }
      : request
};

// Windows authentication is a supported scheme and the API's default, and the
// web app is served from a different origin to the API (see appconfig.json),
// so the browser only sends Negotiate/NTLM credentials when the request opts
// in. The generated runtime does not set this, and it is generated code that
// CI regenerates and diff-gates, so it cannot be hand-edited — middleware is
// the supported seam. Without it a WinAuth deployment 401s on every call.
const withCredentialsMiddleware = {
  pre: (request: AjaxConfig): AjaxConfig => ({
    ...request,
    withCredentials: true
  })
};

// The authentication scheme and API base URL only become known after the
// initial /ApiConfig fetch resolves (see router/index.ts), which is later
// than this module loads — so both parameters are property getters the
// generated Configuration re-reads on every request rather than values
// captured at construction time.
export const dorcApiConfiguration = new Configuration({
  middleware: [withCredentialsMiddleware, sessionRenewingXhrMiddleware],
  get basePath(): string {
    return appConfig.dorcApi;
  },
  get accessToken(): (() => string) | undefined {
    return appConfig.authenticationScheme === OAUTH_SCHEME
      ? oauthAuthorizationHeader
      : undefined;
  }
});
