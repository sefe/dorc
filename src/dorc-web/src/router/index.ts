import './style-registrations';
import { routes } from './routes.ts';
import { router } from './router.ts';
import { appConfig } from '../app-config';
import { ApiConfigApi, ApiConfigModel } from '../apis/dorc-api';
import { OAUTH_SCHEME, oauthServiceContainer } from '../services/Account/OAuthService';

new ApiConfigApi().apiConfigGet().subscribe({
  next: (apiConfig: ApiConfigModel) => {
    appConfig.authenticationScheme = apiConfig.AuthenticationScheme ?? 'NotSet';
    appConfig.oauthAuthority = apiConfig.OAuthAuthority ?? 'NotSet';
    if (appConfig.authenticationScheme == OAUTH_SCHEME) {
      oauthServiceContainer.setAuthority(appConfig.oauthAuthority);
      oauthServiceContainer.service.getUser().subscribe(user => {
        if (!user || !user.access_token) {
          oauthServiceContainer.service.signIn();
        } else {
          router.setRoutes(routes);
        }
      });
    } else {
      router.setRoutes(routes);
    }
  },
  error: (err: string) => console.error(err)
});