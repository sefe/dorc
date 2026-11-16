import { UserManagerSettings, UserManager, User } from 'oidc-client-ts';
import { OAuthConfigurableSettings, oauthSettings } from '../../OAuthSettings';
import { catchError, from, Observable, tap } from 'rxjs';
import { drawerShortcuts } from '../../components/drawer-shortcuts';

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
  private _silentRenewPromise: Promise<User | null> | null = null;
  private _signInRedirectStarted = false;

  constructor(settings: OAuthServiceSettings) {
    this._mgr = new UserManager(settings);
    if (settings.authority && settings.client_id) {
      this.saveConfigurableSettings();
    }
    this._mgr.events.addUserLoaded(user => {
      this._signedInUser = user;
    });
    this._mgr.events.addUserUnloaded(() => {
      this._signedInUser = null;
    });
    this._mgr.events.addAccessTokenExpiring(() => this.accessTokenExpiring());
    this._mgr.events.addAccessTokenExpired(() => this.accessTokenExpired());
  }

  /**
   * Signs in a user to IdentityServer. Triggers a redirect of the current window to the authorization endpoint.
   */
  public signIn(): void {
    if (this._signInRedirectStarted) {
      return;
    }
    this._signInRedirectStarted = true;
    console.log('signin redirect');
    localStorage.setItem('lastUrl', window.location.href);
    this.saveConfigurableSettings();
    this._mgr.signinRedirect().catch(err => {
      this._signInRedirectStarted = false;
      console.error(err);
    });
  }

  private saveConfigurableSettings() {
    const configurableSettings: OAuthConfigurableSettings = {
      authority: this._mgr.settings.authority,
      client_id: this._mgr.settings.client_id,
      scope: this._mgr.settings.scope
    };
    localStorage.setItem(
      'idsrv.oauthsettings',
      JSON.stringify(configurableSettings)
    );
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
        // Silent token renewal loads signin-callback.html inside a hidden iframe.
        // oidc-client-ts posts the callback result to the parent frame during
        // signinCallback(); navigating or clearing storage inside the iframe
        // would clobber the parent session state.
        if (window.self !== window.top) {
          return;
        }
        const lastUrl = localStorage.getItem('lastUrl') ?? '/';
        localStorage.removeItem('lastUrl');
        const exceptions = ['signin.html'];
        if (exceptions.some(exception => lastUrl.endsWith(exception))) {
          location.assign('/');
        } else {
          location.assign(lastUrl);
        }
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
    // Both sign-out paths clear shortcuts: this one, and signOutCallback below
    // for the return leg. Neither covers a browser close or a silent session
    // expiry — see SC-17, which states that limit rather than implying coverage.
    drawerShortcuts.clear();
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
        localStorage.removeItem('idsrv.oauthsettings');
        // SC-17: shortcuts used to live in a cookie with an accidental 7-day TTL,
        // which was the only thing that ever pruned them. localStorage has no
        // expiry, so without this they would outlive the session indefinitely.
        // Cleared to empty rather than removed, so the migration guard stays armed.
        drawerShortcuts.clear();
        location.assign('/');
      })
      .catch(err => console.error(err));
  }

  /**
   * Silently renews the current session, deduplicating concurrent renewal calls.
   */
  public renewSession(): Promise<OAuthServiceUser | null> {
    if (this._silentRenewPromise) {
      return this._silentRenewPromise;
    }
    this.saveConfigurableSettings();
    this._silentRenewPromise = this._mgr
      .signinSilent()
      .then(user => {
        this._signedInUser = user;
        console.log('silent renew success');
        return user;
      })
      .catch(err => {
        console.log('silent renew error', err);
        return null;
      })
      .finally(() => {
        this._silentRenewPromise = null;
      });
    return this._silentRenewPromise;
  }

  /**
   * Attempts to silently renew an expired session and falls back to an
   * interactive sign-in redirect when silent renewal cannot recover it.
   */
  public recoverExpiredSession(
    onFallbackSignIn?: () => void
  ): Promise<OAuthServiceUser | null> {
    return this.renewSession().then(user => {
      if (user && user.access_token && !user.expired) {
        return user;
      }
      if (onFallbackSignIn) {
        onFallbackSignIn();
      } else {
        this.signIn();
      }
      return null;
    });
  }

  /**
   * Handle an expiration of `access_token` event by silently initiating a "Refresh Token flow"
   */
  private accessTokenExpiring() {
    console.log('token expiring');
    void this.renewSession();
  }

  private accessTokenExpired() {
    console.log('token expired');
    void this.recoverExpiredSession();
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
