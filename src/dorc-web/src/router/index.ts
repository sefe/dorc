import './style-registrations';
import { routes } from './routes.ts';
import { router } from './router.ts';
import { appConfig } from '../app-config';
import { ApiConfigApi, ApiConfigModel } from '../apis/dorc-api';
import { OAUTH_SCHEME, oauthServiceContainer, OAuthServiceSettings } from '../services/Account/OAuthService';
import { oauthSettings } from '../OAuthSettings.ts';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

const routeConfig = routes;

new ApiConfigApi(dorcApiConfiguration).apiConfigGet().subscribe({
  next: (apiConfig: ApiConfigModel) => {
    appConfig.authenticationScheme = apiConfig.AuthenticationScheme ?? 'NotSet';
    appConfig.pauseDeploymentEnabled = Boolean((apiConfig as Record<string, unknown>)['PauseDeploymentEnabled']);
    appConfig.isProduction = Boolean((apiConfig as Record<string, unknown>)['IsProduction']);
    if (appConfig.authenticationScheme == OAUTH_SCHEME) {
      const settings: OAuthServiceSettings = {
        ...oauthSettings,
        authority: apiConfig.OAuthAuthority ?? '',
        client_id: apiConfig.OAuthUiClientId ?? '',
        scope: apiConfig.OAuthUiRequestedScopes ?? ''
      };
      oauthServiceContainer.setSettings(settings);
      oauthServiceContainer.service.getUser().subscribe({
        next: (user) => {
          if (!user || !user.access_token) {
            oauthServiceContainer.service.signIn();
          } else {
            void router.setRoutes(routeConfig);
          }
        },
        error: (err) => console.error('Error getting user:', err)
      });
    } else {
      void router.setRoutes(routeConfig);
    }
  },
  error: (err: string) => console.error(err)
}); 