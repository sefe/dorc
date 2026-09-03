import { ApiConfigApi, ApiConfigModel } from './apis/dorc-api';
import { oauthSettings } from './OAuthSettings';
import { oauthServiceContainer, OAuthServiceSettings } from './services/Account/OAuthService';
import { dorcApiConfiguration } from './services/dorc-api-configuration';

const signInButton = document.getElementById("signinButton") as HTMLButtonElement;
signInButton.addEventListener("click", () => {
    new ApiConfigApi(dorcApiConfiguration).apiConfigGet().subscribe(
        (apiConfig: ApiConfigModel) => {
            const settings: OAuthServiceSettings = {
                ...oauthSettings,
                authority: apiConfig.OAuthAuthority ?? '',
                client_id: apiConfig.OAuthUiClientId ?? '',
                scope: apiConfig.OAuthUiRequestedScopes ?? ''
            };
            oauthServiceContainer.setSettings(settings);
            oauthServiceContainer.service.signIn();
        }
    );
});