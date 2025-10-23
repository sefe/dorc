import { OAuthServiceSettings } from "./services/Account/OAuthService";

const url = window.location.origin;

export const oauthSettings: OAuthServiceSettings = {
    authority: "",
    client_id: "",
    scope: "",
    //resource: "dorc-api",
    redirect_uri: url + "/signin-callback.html",
    silent_redirect_uri: url + "/signin-callback.html",
    post_logout_redirect_uri: url + "/signout-callback.html"
  };

  export declare interface OAuthConfigurableSettings {
    authority: string;
    client_id: string;
    scope?: string;
  }