import { OAuthConfigurableSettings, oauthSettings } from './OAuthSettings';
import { oauthServiceContainer } from './services/Account/OAuthService';

const configurableSettings: OAuthConfigurableSettings = JSON.parse(
  localStorage.getItem('idsrv.oauthsettings') ?? '{}'
);
const settings = {
  ...oauthSettings,
  ...configurableSettings
};
oauthServiceContainer.setSettings(settings);
oauthServiceContainer.service.signOutCallback();
