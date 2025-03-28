import { OAuthServiceSettings } from "./services/Account/OAuthService";

const url = window.location.origin;

export const oauthSettings: OAuthServiceSettings = {
    authority: "",
    client_id: "dorc-ui",
    //resource: "dorc-api",
    scope: "openid profile offline_access email dorc-api.manage",
    redirect_uri: url + "/signin-callback.html",
    silent_redirect_uri: url + "/signin-callback.html",
    post_logout_redirect_uri: url + "/signout-callback.html",
    automaticSilentRenew: false
  };