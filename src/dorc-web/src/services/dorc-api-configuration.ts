import { Configuration } from '../apis/dorc-api';
import { appConfig } from '../app-config';
import { OAUTH_SCHEME, oauthServiceContainer } from './Account/OAuthService';

function oauthAuthorizationHeader(): string {
  const user = oauthServiceContainer.service.signedInUser;
  if (!user || user.expired) {
    location.assign('/signin.html');
    return '';
  }
  return `Bearer ${user.access_token}`;
}

// The authentication scheme and API base URL only become known after the
// initial /ApiConfig fetch resolves (see router/index.ts), which is later
// than this module loads — so both parameters are property getters the
// generated Configuration re-reads on every request rather than values
// captured at construction time.
export const dorcApiConfiguration = new Configuration({
  get basePath(): string {
    return appConfig.dorcApi;
  },
  get accessToken(): (() => string) | undefined {
    return appConfig.authenticationScheme === OAUTH_SCHEME
      ? oauthAuthorizationHeader
      : undefined;
  }
});
