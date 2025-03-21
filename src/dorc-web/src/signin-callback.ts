import { oauthServiceContainer } from "./services/Account/OAuthService";

const authority = localStorage.getItem("idsrv.authority") ?? '';
oauthServiceContainer.setAuthority(authority);
oauthServiceContainer.service.signInCallback();