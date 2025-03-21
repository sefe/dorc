import { ApiConfigApi, ApiConfigModel } from "./apis/dorc-api";
import { oauthServiceContainer } from "./services/Account/OAuthService";

const signInButton = document.getElementById("signinButton") as HTMLButtonElement;
signInButton.addEventListener("click", () => {
    new ApiConfigApi().apiConfigGet().subscribe(
        (apiConfig: ApiConfigModel) => {
            oauthServiceContainer.setAuthority(apiConfig.OAuthAuthority ?? 'NotSet');
            oauthServiceContainer.service.signIn();
        }
    );
});