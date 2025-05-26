import { UserManagerSettings, UserManager, User } from 'oidc-client-ts';
import { OAuthConfigurableSettings, oauthSettings } from '../../OAuthSettings';
import { catchError, from, Observable, tap } from 'rxjs';

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
   * Signs in a user to IdentityServer. Triggers a redirect of the current window to the authorization endpoint.
   */
  public signIn(): void {
    console.log('signin redirect');
    localStorage.setItem("lastUrl", window.location.href);
    this.saveConfigurableSettings();
    this._mgr.signinRedirect().catch(err => console.error(err));
  }

  private saveConfigurableSettings() {
    const configurableSettings: OAuthConfigurableSettings = {
      authority: this._mgr.settings.authority,
      client_id: this._mgr.settings.client_id,
      scope: this._mgr.settings.scope
    };
    localStorage.setItem("idsrv.oauthsettings", JSON.stringify(configurableSettings));
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
        localStorage.removeItem("idsrv.oauthsettings");
        const lastUrl = localStorage.getItem("lastUrl") ?? '/';
        localStorage.removeItem("lastUrl");
        location.assign(lastUrl);
      })
      .catch(err => console.error(err));
  }

  /**
   * Load the `OAuthServiceUser` object for the currently authenticated user.
   * @returns an Observable containing either `signedInUser` or null if user is not authenticated
   */
  public getUser(): Observable<OAuthServiceUser | null> {
    return from(this._mgr.getUser()).pipe(
      tap(user => (this._signedInUser = user))
    );
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
   * Signes out the user from Identity Provider
   */
  public signOut(): Observable<void> {
    this.saveConfigurableSettings();
    return from(this._mgr.signoutRedirect()).pipe(
      tap(() => {
        console.log('signed out');
      }),
      catchError(err => {
        console.error(err);
        throw err;
      })
    );
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
        localStorage.removeItem("idsrv.oauthsettings");
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

  public setSettings(settings: OAuthServiceSettings): void {
    this._oauthService = new OAuthService(settings);
  }
}

export const oauthServiceContainer = new OAuthServiceContainer();