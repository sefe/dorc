import { ApiConfigApi, ApiConfigModel } from './apis/dorc-api';
import { oauthSettings } from './OAuthSettings';
import { oauthServiceContainer, OAuthServiceSettings } from './services/Account/OAuthService';

const signInButton = document.getElementById("signinButton") as HTMLButtonElement;
signInButton.addEventListener("click", () => {
    new ApiConfigApi().apiConfigGet().subscribe(
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