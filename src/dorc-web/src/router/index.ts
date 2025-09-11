import './style-registrations';
import { routes } from './routes.ts';
import { router } from './router.ts';
import { appConfig } from '../app-config';
import { ApiConfigApi, ApiConfigModel } from '../apis/dorc-api';
import {
  OAUTH_SCHEME,
  oauthServiceContainer,
  OAuthServiceSettings
} from '../services/Account/OAuthService';
import { oauthSettings } from '../OAuthSettings.ts';

new ApiConfigApi().apiConfigGet().subscribe({
  next: (apiConfig: ApiConfigModel) => {
    appConfig.authenticationScheme = apiConfig.AuthenticationScheme ?? 'NotSet';
    if (appConfig.authenticationScheme == OAUTH_SCHEME) {
      const settings: OAuthServiceSettings = {
        ...oauthSettings,
        authority: apiConfig.OAuthAuthority ?? '',
        client_id: apiConfig.OAuthUiClientId ?? '',
        scope: apiConfig.OAuthUiRequestedScopes ?? ''
      };
      oauthServiceContainer.setSettings(settings);
      oauthServiceContainer.service.getUser().subscribe({
        next: user => {
          if (!user || !user.access_token) {
            oauthServiceContainer.service.signIn();
          } else {
            router.setRoutes(routes);
          }
        },
        error: err => console.error('Error getting user:', err)
      });
    } else {
      router.setRoutes(routes);
    }
  },
  error: (err: string) => console.error(err)
});
