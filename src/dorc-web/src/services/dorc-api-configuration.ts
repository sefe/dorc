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

function oauthAuthorizationHeader(): string {
  const user = oauthServiceContainer.service.signedInUser;
  if (!user) {
    redirectToSignIn();
    return '';
  }
  // An expired token is still sent rather than short-circuited: OAuthService
  // renews silently on the accessTokenExpiring event, and during that window
  // user.expired is true even though the session is recoverable. Redirecting
  // here would throw the user out mid-renewal.
  return `Bearer ${user.access_token}`;
}

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
  middleware: [withCredentialsMiddleware],
  get basePath(): string {
    return appConfig.dorcApi;
  },
  get accessToken(): (() => string) | undefined {
    return appConfig.authenticationScheme === OAUTH_SCHEME
      ? oauthAuthorizationHeader
      : undefined;
  }
});
