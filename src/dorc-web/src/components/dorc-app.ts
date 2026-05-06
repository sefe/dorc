import { css, PropertyValues } from 'lit';
import { html } from 'lit/html.js';
import { customElement, property, query, state } from 'lit/decorators.js';
import '@vaadin/button';
import { MakeLikeProdApi, RefDataRolesApi, MetadataApi } from '../apis/dorc-api';
import './dorc-navbar.ts';
import { DorcNavbar } from './dorc-navbar.ts';
import './theme-toggle.ts';
import { themeManager } from '../theme/theme-manager.ts';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { ShortcutsStore } from './shortcuts-store.ts';
import { appConfig } from '../app-config.ts';
import { OAUTH_SCHEME, oauthServiceContainer } from '../services/Account/OAuthService.ts';
import { NARROW_BREAKPOINT } from '../helpers/responsive-mixin.ts';

let dorcNavbar: DorcNavbar;

function fMouseMoveListener(event: MouseEvent) {
  const width = Math.max(200, Math.min(1000, event.clientX));
  const widthInPx = `${width}px`;

  requestAnimationFrame(() => {
    dorcNavbar.style.width = widthInPx;
  });
}
// Invoked from `_wrappedMouseUpListener` in DorcApp. The wrapper owns
// registration/removal of itself; this function only handles the splitter
// drag teardown (release the global user-select lock and the mousemove
// handler) and commits the final width.
function fMouseUpListener(event: MouseEvent) {
  document.body.style.removeProperty('user-select');
  document.body.removeEventListener('mousemove', fMouseMoveListener);

  const width = Math.max(200, Math.min(1000, event.clientX));
  dorcNavbar.style.width = width + 'px';
}

@customElement('dorc-app')
export class DorcApp extends ShortcutsStore {
  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        height: 100vh;
        height: 100dvh;
        margin: 0;
        background: var(--dorc-bg-primary);
        font-family: var(--lumo-font-family, Arial, sans-serif);
        overflow: hidden;
      }

      #header {
        height: var(--dorc-header-height, 50px);
        flex-shrink: 0;
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 0 12px;
        background: var(--dorc-bg-secondary);
        color: var(--dorc-text-secondary);
        box-sizing: border-box;
      }

      #header .menu-btn {
        flex-shrink: 0;
      }

      #header .mascot {
        height: 65px;
        padding: 3px 0;
        flex-shrink: 0;
      }

      #header .app-title {
        font-size: 1.25rem;
        font-weight: 600;
        color: var(--dorc-text-primary);
        white-space: nowrap;
      }

      #header .env-warning {
        font-size: 1rem;
        font-weight: 600;
        padding: 4px 10px;
        border-radius: 4px;
        color: #fff;
        background: var(--dorc-error-color);
        white-space: nowrap;
      }

      #header .spacer {
        flex: 1 1 auto;
      }

      #header .user-info {
        flex-shrink: 0;
        text-align: right;
        font-size: 0.75rem;
        color: var(--dorc-text-secondary);
        line-height: 1.4;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        max-width: 300px;
      }

      #header .header-link {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        color: var(--dorc-link-color);
        text-decoration: none;
        white-space: nowrap;
      }

      #header .header-link:hover {
        text-decoration: underline;
      }

      #page {
        display: flex;
        flex: 1;
        min-height: 0;
      }

      #dorcNavbar {
        width: var(--dorc-sidebar-width, 300px);
        flex-shrink: 0;
        overflow: hidden;
        transition: width 0.2s ease;
      }

      #splitter {
        width: 2px;
        min-width: 2px;
        flex-shrink: 0;
        cursor: ew-resize;
        padding: 4px 0 0;
        top: 0;
        right: 0;
        bottom: 0;
        background-color: var(--dorc-bg-secondary);
      }

      #page-content {
        background: var(--dorc-bg-primary);
        overflow: auto;
        flex: 1;
        min-width: 0;
      }

      @media (max-width: 768px) {
        #dorcNavbar {
          position: fixed;
          top: var(--dorc-header-height, 50px);
          left: 0;
          bottom: 0;
          z-index: 100;
          width: 0;
          max-width: 85vw;
          background: var(--dorc-bg-primary);
          box-shadow: 2px 0 8px rgba(0, 0, 0, 0.15);
          visibility: hidden;
          pointer-events: none;
          transform: translateX(-100%);
          transition: transform 0.2s ease, width 0.2s ease, visibility 0s linear 0.2s;
        }

        #dorcNavbar.open {
          width: var(--dorc-sidebar-width, 300px);
          visibility: visible;
          pointer-events: auto;
          transform: translateX(0);
          transition: transform 0.2s ease, width 0.2s ease, visibility 0s;
        }

        #splitter {
          display: none;
        }

        #header .user-info {
          display: none;
        }

        #header {
          padding: 0 8px;
        }
      }
    `;
  }

  @property() userEmail = '';
  @property() userRoles = '';
  @property() dorcEnv = '';

  @query('#splitter') splitter!: HTMLDivElement;

  @state() private _drawerOpen = false;
  @state() private _narrowScreen = false;

  private _narrowMq: MediaQueryList | undefined;
  private _previouslyFocused: HTMLElement | null = null;
  private _drawerLockedScroll = false;
  private _splitterDragInProgress = false;

  private _narrowMqHandler = (e: MediaQueryListEvent) => {
    this._narrowScreen = e.matches;
    // Only clear inline width when ENTERING mobile, so the desktop CSS can
    // take over the drawer. On wide, leaving the user's splitter-dragged
    // width intact (the alternative would silently reset it on every resize
    // across the breakpoint).
    if (e.matches && this.dorcNavbar) {
      this.dorcNavbar.style.width = '';
    }
    if (!this._narrowScreen && this._drawerOpen) {
      // Drawer is always reachable on desktop; collapse the mobile-modal state.
      this._closeDrawer();
    }
    this._applyDrawerAria();
  };
  private _routerLocationChanged = () => {
    if (this._narrowScreen && this._drawerOpen) {
      this._closeDrawer();
    }
  };
  private _keydownHandler = (e: KeyboardEvent) => {
    if (e.key === 'Escape' && this._narrowScreen && this._drawerOpen) {
      this._closeDrawer();
    }
  };
  // Pull focus back into the drawer when it tries to escape on mobile.
  // Cheaper than enumerating focusables across nested shadow roots.
  private _focusGuardHandler = (e: FocusEvent) => {
    if (!this._drawerOpen || !this._narrowScreen || !this.dorcNavbar) return;
    if (e.composedPath().includes(this.dorcNavbar)) return;
    this.dorcNavbar.focus();
  };

  render() {
    return html`
      <header id="header" role="banner">
        <vaadin-button
          class="menu-btn"
          theme="icon"
          aria-label="Toggle Menu"
          aria-expanded="${this._drawerOpen ? 'true' : 'false'}"
          @click="${this.toggleSideBar}"
        >
          <vaadin-icon icon="lumo:menu"></vaadin-icon>
        </vaadin-button>
        <img
          class="mascot"
          src="/hegsie_white_background_cartoon_dork_code_markdown_simple_icon__ef4f70a2-200b-4a67-82ba-73b12eb495d3.png"
          alt="DOrc mascot"
        />
        ${appConfig.isProduction
          ? html`<span class="app-title" title="DevOps Orchestrator">DOrc</span>`
          : html`<span class="env-warning" title="DevOps Orchestrator"
              >${this.dorcEnv} - Non-Prod Instance</span
            >`}
        <div class="spacer"></div>
        <div class="user-info">
          <div>${this.userEmail}</div>
          <div>${this.userRoles}</div>
        </div>
        <vaadin-button
          ?hidden="${!this.showSignOutButton}"
          @click="${this.signOut}"
          >Sign Out</vaadin-button
        >
        <theme-toggle></theme-toggle>
        <a
          class="header-link"
          href="${this.dorcHelperPage}"
          target="_blank"
          rel="noopener noreferrer"
        >
          <vaadin-icon icon="vaadin:info-circle"></vaadin-icon>
          Help
        </a>
      </header>

      <div id="page">
        <dorc-navbar id="dorcNavbar"></dorc-navbar>
        <div id="splitter"></div>
        <div id="page-content">
          <slot></slot>
        </div>
      </div>
    `;
  }

  constructor() {
    super();
    themeManager.init();
    this.getUserEmail();
    this.getUserRoles();
    this.getDorcEnv();
    this.dorcHelperPage = appConfig.dorcHelperPage;
  }

  connectedCallback() {
    super.connectedCallback();
    this._narrowMq = window.matchMedia(`(max-width: ${NARROW_BREAKPOINT}px)`);
    this._narrowScreen = this._narrowMq.matches;
    this._narrowMq.addEventListener('change', this._narrowMqHandler);
    window.addEventListener(
      'vaadin-router-location-changed',
      this._routerLocationChanged
    );
    document.addEventListener('keydown', this._keydownHandler);
    // After a disconnect/reconnect cycle, firstUpdated does not re-fire but
    // the splitter element still exists in our shadow DOM. Re-attach the
    // mousedown listener once the next render has settled. Catch rejections
    // (render errors) so they're surfaced rather than swallowed silently.
    this.updateComplete
      .then(() => this._attachSplitterListener())
      .catch(err => console.error('dorc-app deferred splitter attach failed:', err));
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    // Assign dorcNavbar BEFORE calling super to ensure it's available for event handlers
    this.dorcNavbar = this.shadowRoot?.getElementById(
      'dorcNavbar'
    ) as DorcNavbar;
    dorcNavbar = this.dorcNavbar;

    super.firstUpdated(_changedProperties);

    this._applyDrawerAria();
    this._attachSplitterListener();
  }

  // Splitter mousedown handler is a class field so it has a stable reference
  // across attach/detach cycles (firstUpdated only fires once per element).
  private _splitterMouseDownHandler = () => {
    this._splitterDragInProgress = true;
    document.body.addEventListener('mousemove', fMouseMoveListener, {
      passive: true
    });
    document.body.addEventListener('mouseup', this._wrappedMouseUpListener);
    document.body.style.setProperty('user-select', 'none');
  };

  // Idempotent: removeEventListener with an unregistered handler is a no-op,
  // so calling this from both firstUpdated and connectedCallback is safe.
  private _attachSplitterListener() {
    if (!this.splitter) return;
    this.splitter.removeEventListener('mousedown', this._splitterMouseDownHandler);
    this.splitter.addEventListener('mousedown', this._splitterMouseDownHandler);
  }

  // Wrapper around fMouseUpListener that also clears the in-progress flag so
  // disconnectedCallback can tell whether to release body styles.
  private _wrappedMouseUpListener = (event: MouseEvent) => {
    document.body.removeEventListener('mouseup', this._wrappedMouseUpListener);
    this._splitterDragInProgress = false;
    fMouseUpListener(event);
  };

  disconnectedCallback() {
    super.disconnectedCallback();
    this._narrowMq?.removeEventListener('change', this._narrowMqHandler);
    window.removeEventListener(
      'vaadin-router-location-changed',
      this._routerLocationChanged
    );
    document.removeEventListener('keydown', this._keydownHandler);
    document.removeEventListener('focusin', this._focusGuardHandler, true);
    if (this.splitter) {
      this.splitter.removeEventListener('mousedown', this._splitterMouseDownHandler);
    }
    // Only release body styles we own, so coexisting modals/drags aren't clobbered.
    if (this._splitterDragInProgress) {
      document.body.removeEventListener('mousemove', fMouseMoveListener);
      document.body.removeEventListener('mouseup', this._wrappedMouseUpListener);
      document.body.style.removeProperty('user-select');
      this._splitterDragInProgress = false;
    }
    if (this._drawerLockedScroll) {
      document.body.style.removeProperty('overflow');
      this._drawerLockedScroll = false;
    }
  }

  private toggleSideBar() {
    if (!this.dorcNavbar) return;
    if (this._narrowScreen) {
      // Clear any desktop/splitter inline width so mobile CSS can control the drawer
      this.dorcNavbar.style.width = '';
      if (this._drawerOpen) {
        this._closeDrawer();
      } else {
        this._openDrawer();
      }
    } else {
      const sidebarWidth =
        getComputedStyle(this).getPropertyValue('--dorc-sidebar-width').trim() ||
        '300px';
      if (this.dorcNavbar.style.width === '0px') {
        this.dorcNavbar.style.width = sidebarWidth;
      } else {
        this.dorcNavbar.style.width = '0px';
      }
    }
  }

  private _openDrawer() {
    if (!this.dorcNavbar) return;
    this._drawerOpen = true;
    this.dorcNavbar.classList.add('open');
    if (this._narrowScreen) {
      this._previouslyFocused = this._activeFocusedElement();
      document.body.style.overflow = 'hidden';
      this._drawerLockedScroll = true;
      document.addEventListener('focusin', this._focusGuardHandler, true);
      // Move focus into the drawer so AT users land inside the modal.
      this.dorcNavbar.tabIndex = -1;
      this.dorcNavbar.focus();
    }
    this._applyDrawerAria();
  }

  private _closeDrawer() {
    if (!this.dorcNavbar) return;
    this._drawerOpen = false;
    this.dorcNavbar.classList.remove('open');
    if (this._drawerLockedScroll) {
      document.body.style.removeProperty('overflow');
      this._drawerLockedScroll = false;
    }
    document.removeEventListener('focusin', this._focusGuardHandler, true);
    this._applyDrawerAria();
    // Restore focus to whatever opened the drawer (typically the menu button).
    // Use `isConnected` rather than `document.contains` because the latter
    // doesn't traverse shadow boundaries — and `_activeFocusedElement()`
    // returns the deepest shadow-root activeElement, which is the common case
    // for openers inside Vaadin custom elements.
    // If the element is gone (SPA navigation, re-render), fall back to the
    // menu button so AT users have a sensible landing point.
    const toFocus = this._previouslyFocused;
    this._previouslyFocused = null;
    if (toFocus && toFocus.isConnected && typeof toFocus.focus === 'function') {
      toFocus.focus();
    } else {
      const menuBtn = this.shadowRoot?.querySelector(
        '.menu-btn'
      ) as HTMLElement | null;
      menuBtn?.focus();
    }
  }

  // Walk composed-path to find the active element across shadow roots,
  // so we can restore focus precisely on close.
  private _activeFocusedElement(): HTMLElement | null {
    let el = document.activeElement as HTMLElement | null;
    while (el && el.shadowRoot && el.shadowRoot.activeElement) {
      el = el.shadowRoot.activeElement as HTMLElement;
    }
    return el;
  }

  // Drawer is reachable on desktop regardless of `_drawerOpen`; on mobile we
  // hide it from AT and the tab order when closed, and announce it as a modal
  // dialog when open.
  private _applyDrawerAria() {
    if (!this.dorcNavbar) return;
    if (this._narrowScreen) {
      if (this._drawerOpen) {
        this.dorcNavbar.removeAttribute('inert');
        this.dorcNavbar.removeAttribute('aria-hidden');
        this.dorcNavbar.setAttribute('role', 'dialog');
        this.dorcNavbar.setAttribute('aria-modal', 'true');
        this.dorcNavbar.setAttribute('aria-label', 'Navigation');
      } else {
        this.dorcNavbar.setAttribute('inert', '');
        this.dorcNavbar.setAttribute('aria-hidden', 'true');
        this.dorcNavbar.removeAttribute('role');
        this.dorcNavbar.removeAttribute('aria-modal');
        this.dorcNavbar.removeAttribute('aria-label');
      }
    } else {
      this.dorcNavbar.removeAttribute('inert');
      this.dorcNavbar.removeAttribute('aria-hidden');
      this.dorcNavbar.removeAttribute('role');
      this.dorcNavbar.removeAttribute('aria-modal');
      this.dorcNavbar.removeAttribute('aria-label');
      document.body.style.removeProperty('overflow');
    }
  }

  private getUserRoles() {
    const api = new RefDataRolesApi();
    api.refDataRolesGet().subscribe({
      next: (data: string[]) => {
        this.userRoles = data.join(' | ');
      },
      error: (err: string) => console.error(err)
    });
  }

  private getUserEmail() {
    const api = new MakeLikeProdApi();
    api.makeLikeProdNotifyEmailAddressGet().subscribe({
      next: value => {
        this.userEmail = value;
      },
      error: (err: string) => console.error(err),
    });
  }

  private getDorcEnv() {
    const api = new MetadataApi();
    api.metadataGet().subscribe({
      next: (data: string) => {
        this.dorcEnv = data.split('-')[0].trim();
      },
      error: (err: string) => console.error(err)
    });
  }

  @property({ type: Boolean }) showSignOutButton = appConfig.authenticationScheme == OAUTH_SCHEME;

  private signOut() {
    oauthServiceContainer.service.signOut();
  }
}