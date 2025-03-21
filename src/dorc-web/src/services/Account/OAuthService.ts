import { UserManagerSettings, UserManager, User } from 'oidc-client-ts';
import { oauthSettings } from '../../OAuthSettings';

export const OAUTH_SCHEME = 'OAuth';

/**
 * Provides information about a User and their tokens
 *
 * @public
 */
export type OAuthServiceUser = User;

/**
 * The settings used to configure the {@link OAuthService}.
 *
 * @public
 */
export type OAuthServiceSettings = UserManagerSettings;

/**
 * Provides a higher level API for signing a user in, signing out,
 * and reading an access token returned from the identity provider (OAuth2/OIDC).
 *
 * @public
 */
export class OAuthService {
  private _mgr: UserManager;
  private _signedInUser: User | null = null;

  constructor(settings: OAuthServiceSettings) {
    this._mgr = new UserManager(settings);
    this._mgr.events.addAccessTokenExpiring(() => this.accessTokenExpiring());
  }

  /**
   * Signes-In a user to IdentityServer. Trigger a redirect of the current window to the authorization endpoint.
   */
  public signIn(): void {
    console.log('signin redirect');
    localStorage.setItem("idsrv.authority", this._mgr.settings.authority);
    this._mgr.signinRedirect().catch(err => console.error(err));
  }

  /**
   * Process any response (callback) from the authorization endpoint, by dispatching the request_type
   * and redirect the application back to tha main page (`location.assign('/')`)
   */
  public signInCallback(): void {
    this._mgr
      .signinCallback()
      .then((user: User | undefined) => {
        if (user) {
          this._signedInUser = user;
        }
        console.log('signin response success');
        localStorage.removeItem("idsrv.authority");
        location.assign('/');
      })
      .catch(err => console.error(err));
  }

  /**
   * Load the `OAuthServiceUser` object for the currently authenticated user.
   * @returns a promise containing either `signedInUser` or null if user is not authenticated
   */
  public async getUser(): Promise<OAuthServiceUser | null> {
    return this._mgr.getUser().then(user => (this._signedInUser = user));
  }

  /**
   * Get the `OAuthServiceUser` object for the currently authenticated user.
   * @returns either `signedInUser` or null if user is not authenticated
   */
  public get signedInUser(): OAuthServiceUser | null {
    if (!this._signedInUser) {
      this.getUser();
    }
    return this._signedInUser;
  }

  /**
   * Signes-Out the user from IdentityServer
   */
  public async signOut(): Promise<void> {
    localStorage.setItem("idsrv.authority", this._mgr.settings.authority);
    return this._mgr
      .signoutRedirect()
      .then(() => console.log('signed out'))
      .catch(err => console.error(err));
  }

  /**
   * Process any response (callback) from the end session endpoint, by dispatching the request_type
   * and redirect the application back to tha main page (`location.assign('/')`)
   */
  public signOutCallback(): void {
    this._mgr
      .signoutCallback()
      .then(() => {
        console.log('signout callback response success');
        localStorage.removeItem("idsrv.authority");
        location.assign('/');
      })
      .catch(err => console.error(err));
  }

  /**
   * Handle an expiration of `access_token` event by silently initiating a "Refresh Token flow"
   */
  private accessTokenExpiring() {
    console.log('token expiring');
    this._mgr
      .signinSilent()
      .then(user => {
        this._signedInUser = user;
        console.log('silent renew success');
      })
      .catch(err => console.log('silent renew error', err));
  }
}

export class OAuthServiceContainer {
  private _oauthService: OAuthService;

  constructor() {
    this._oauthService = new OAuthService(oauthSettings);
  }

  get service(): OAuthService {
    return this._oauthService;
  }

  public setAuthority(authority: string): void {
    this._oauthService = new OAuthService({
      ...oauthSettings,
      authority: authority
    });
  }
}

export const oauthServiceContainer = new OAuthServiceContainer();