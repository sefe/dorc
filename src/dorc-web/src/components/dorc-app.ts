import { css, LitElement, PropertyValues } from 'lit';
import { html } from 'lit/html.js';
import { customElement, property, query, state } from 'lit/decorators.js';
import '@vaadin/button';
import {
  MakeLikeProdApi,
  RefDataRolesApi,
  MetadataApi
} from '../apis/dorc-api';
import './dorc-navbar.ts';
import { DorcNavbar } from './dorc-navbar.ts';
import './theme-toggle.ts';
import { themeManager } from '../theme/theme-manager.ts';
import '@vaadin/vaadin-lumo-styles/icons.js';
import {
  drawerShortcuts,
  toEnvShortcut,
  toProjectShortcut,
  toResultShortcut
} from './drawer-shortcuts.ts';
import type {
  DeploymentRequestApiModel,
  EnvironmentApiModel,
  ProjectApiModel
} from '../apis/dorc-api';
import { EnvPageTabNames } from '../pages/page-environment.ts';
import { appConfig } from '../app-config.ts';
import {
  OAUTH_SCHEME,
  oauthServiceContainer
} from '../services/Account/OAuthService.ts';
import { NARROW_BREAKPOINT } from '../helpers/responsive-mixin.ts';
import { LOCATION_CHANGED_EVENT, navigate } from '../router/router.ts';
import { dorcEnvironmentNameFromMetadata } from '../helpers/dorc-environment-name';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

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

  const width = Math.max(200, Math.min(1000, event.clientX));
  dorcNavbar.style.width = width + 'px';
}

@customElement('dorc-app')
export class DorcApp extends LitElement {
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

      /* Skip link (WCAG 2.4.1 Bypass Blocks, Level A).
         The drawer is 21 primary entries plus a shortcut per open environment,
         project and monitor result — each shortcut contributing a link AND a close
         button — so there can be well over a hundred tab stops before page content
         on every route. Visually hidden until focused, then shown. */
      .skip-link {
        position: absolute;
        left: -9999px;
        top: 0;
        z-index: 200;
        padding: 8px 16px;
        background: var(--dorc-bg-secondary);
        color: var(--dorc-text-primary);
        border: 1px solid var(--dorc-border-color);
        border-radius: 4px;
        text-decoration: none;
      }

      .skip-link:focus {
        left: 8px;
        top: 8px;
      }

      #header {
        height: var(--dorc-header-height, 50px);
        flex-shrink: 0;
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 0 12px;
        background: var(--dorc-bg-secondary);
        /* D-23a: 4.5:1 per WCAG 1.4.3 — the plain token is 3.76:1 here. */
        color: var(--dorc-text-secondary-strong);
        box-sizing: border-box;
      }

      #header .menu-btn {
        flex-shrink: 0;
      }

      /* Was height:65px + 6px padding = 71px inside a 50px header with no
         overflow, so it bled ~10px over the drawer and was clipped at the top
         by the host's overflow:hidden (D-33). */
      #header .mascot {
        height: 100%;
        max-height: calc(var(--dorc-header-height, 50px) - 6px);
        width: auto;
        padding: 3px 0;
        flex-shrink: 0;
        box-sizing: border-box;
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
        /* D-23a: 12px text needs 4.5:1. */
        color: var(--dorc-text-secondary-strong);
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

      /* Was a 2px strip of --dorc-bg-secondary between two --dorc-bg-primary
         surfaces: 1.04:1, invisible in both themes, mouse-only, and with no
         separator semantics (D-24). Now a 12px grab area with a centred 2px line
         in a token that clears WCAG 1.4.11's 3:1 in both themes. */
      #splitter {
        position: relative;
        width: 2px;
        min-width: 2px;
        flex-shrink: 0;
        cursor: ew-resize;
        background-color: transparent;
        touch-action: none;
      }

      #splitter::before {
        content: '';
        position: absolute;
        inset-block: 0;
        left: 0;
        width: 2px;
        background-color: var(--dorc-bg-secondary);
      }

      #splitter::after {
        content: '';
        position: absolute;
        inset-block: 0;
        left: -5px;
        width: 12px;
      }

      #splitter:hover::before,
      #splitter:focus-visible::before,
      :host([resizing]) #splitter::before {
        background-color: var(--dorc-icon-interactive);
      }

      #splitter:focus-visible {
        outline: 2px solid var(--dorc-link-color);
        outline-offset: -2px;
      }

      /* D-37: the width transition below applies to the inline width the drag
         writes every frame, so each frame restarted a 200ms ease and the edge
         lagged the cursor throughout. Suppressed while dragging. */
      :host([resizing]) #dorcNavbar {
        transition: none;
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
          transition:
            transform 0.2s ease,
            width 0.2s ease,
            visibility 0s linear 0.2s;
        }

        #dorcNavbar.open {
          width: var(--dorc-sidebar-width, 300px);
          visibility: visible;
          pointer-events: auto;
          transform: translateX(0);
          transition:
            transform 0.2s ease,
            width 0.2s ease,
            visibility 0s;
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

  @property() metaData = '';
  @state() protected dorcHelperPage = '';
  protected dorcNavbar: DorcNavbar | undefined;

  @property() userEmail = '';
  @property() userRoles = '';
  @property() dorcEnv = '';

  @query('#splitter') splitter!: HTMLDivElement;

  @state() private _drawerOpen = false;
  @state() private _narrowScreen = false;
  // Desktop-only: tracks whether the navbar is currently expanded (width > 0)
  // vs collapsed to 0px via the hamburger. Independent of _drawerOpen, which
  // is the mobile-modal state. aria-expanded on the hamburger is derived from
  // whichever is active for the current viewport.
  @state() private _desktopSidebarVisible = true;

  private _narrowMq: MediaQueryList | undefined;
  private _previouslyFocused: HTMLElement | null = null;
  private _drawerLockedScroll = false;
  // Snapshot of document.body.style.overflow taken just before we set our
  // own scroll-lock on mobile drawer open. Restored on close so coexisting
  // modals that locked the page first aren't clobbered when we release.
  private _previousBodyOverflow = '';
  // Only true while a mobile _openDrawer set tabindex on the navbar host;
  // _closeDrawer strips the attribute only when this is set, so we don't
  // clobber a tabindex anyone else may have set.
  private _drawerSetTabindex = false;
  private _splitterDragInProgress = false;
  private _splitterDragMoved = false;
  private _splitterDragStartX = 0;
  private _suppressNextSplitterClick = false;
  private _pageContent: HTMLElement | null = null;

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
      // _closeDrawer() already re-applies the drawer ARIA state and releases
      // any scroll lock we own, so we must not call _applyDrawerAria() again.
      this._closeDrawer();
    } else {
      this._applyDrawerAria();
    }
  };
  private _routerLocationChanged = () => {
    this.dorcNavbar?.setSelectedTab(window.location.pathname);
    if (this._narrowScreen && this._drawerOpen) {
      this._closeDrawer();
    }
  };
  private _keydownHandler = (e: KeyboardEvent) => {
    if (e.key === 'Escape' && this._narrowScreen && this._drawerOpen) {
      this._closeDrawer();
    }
  };

  render() {
    return html`
      <a class="skip-link" href="#page-content" @click="${this.skipToContent}"
        >Skip to main content</a
      >
      <header id="header" role="banner">
        <vaadin-button
          class="menu-btn"
          theme="icon"
          aria-label="Toggle Menu"
          aria-controls="dorcNavbar"
          aria-expanded="${(this._narrowScreen ? this._drawerOpen : this._desktopSidebarVisible) ? 'true' : 'false'}"
          @click="${this.toggleSideBar}"
        >
          <vaadin-icon icon="lumo:menu"></vaadin-icon>
        </vaadin-button>
        <img
          class="mascot"
          src="/hegsie_white_background_cartoon_dork_code_markdown_simple_icon__ef4f70a2-200b-4a67-82ba-73b12eb495d3.png"
          alt="DOrc mascot"
        />
        ${
          appConfig.isProduction
            ? html`<span class="app-title" title="DevOps Orchestrator"
                >DOrc</span
              >`
            : html`<span class="env-warning" title="DevOps Orchestrator"
                >${this.dorcEnv} - Non-Prod Instance</span
              >`
        }
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
        <dorc-navbar
          id="dorcNavbar"
          role="navigation"
          aria-label="Primary"
        ></dorc-navbar>
        <div
          id="splitter"
          role="separator"
          aria-orientation="vertical"
          aria-label="Resize navigation drawer"
          aria-valuemin="200"
          aria-valuemax="1000"
          aria-valuenow="${this._sidebarWidth}"
          tabindex="0"
          @keydown="${this.onSplitterKeydown}"
          @click="${this.cycleSidebarWidth}"
        ></div>
        <div id="page-content" role="main" tabindex="-1">
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
      LOCATION_CHANGED_EVENT,
      this._routerLocationChanged
    );
    document.addEventListener('keydown', this._keydownHandler);
    // After a disconnect/reconnect cycle, firstUpdated does not re-fire but
    // the splitter element still exists in our shadow DOM. Re-attach the
    // mousedown listener once the next render has settled. Catch rejections
    // (render errors) so they're surfaced rather than swallowed silently.
    this.updateComplete
      .then(() => this._attachSplitterListener())
      .catch(err =>
        console.error('dorc-app deferred splitter attach failed:', err)
      );
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    // Assign dorcNavbar BEFORE calling super to ensure it's available for event handlers
    this.dorcNavbar = this.shadowRoot?.getElementById(
      'dorcNavbar'
    ) as DorcNavbar;
    dorcNavbar = this.dorcNavbar;
    this._pageContent = this.shadowRoot?.getElementById('page-content') ?? null;

    super.firstUpdated(_changedProperties);

    this.registerShortcutEvents();
    this._applyDrawerAria();
    this._attachSplitterListener();
  }

  // Splitter mousedown handler is a class field so it has a stable reference
  // across attach/detach cycles (firstUpdated only fires once per element).
  private _splitterMouseDownHandler = (event: MouseEvent) => {
    this._splitterDragInProgress = true;
    this._splitterDragMoved = false;
    this._splitterDragStartX = event.clientX;
    // Suppresses the width transition for the duration of the drag (D-37).
    this.setAttribute('resizing', '');
    document.body.addEventListener(
      'mousemove',
      this._splitterMouseMoveHandler,
      {
        passive: true
      }
    );
    document.body.addEventListener('mouseup', this._wrappedMouseUpListener);
    document.body.style.setProperty('user-select', 'none');
  };

  private _splitterMouseMoveHandler = (event: MouseEvent) => {
    if (Math.abs(event.clientX - this._splitterDragStartX) > 2) {
      this._splitterDragMoved = true;
    }
    fMouseMoveListener(event);
  };

  // Idempotent: removeEventListener with an unregistered handler is a no-op,
  // so calling this from both firstUpdated and connectedCallback is safe.
  private _attachSplitterListener() {
    if (!this.splitter) return;
    this.splitter.removeEventListener(
      'mousedown',
      this._splitterMouseDownHandler
    );
    this.splitter.addEventListener('mousedown', this._splitterMouseDownHandler);
  }

  // Wrapper around fMouseUpListener that also clears the in-progress flag so
  // disconnectedCallback can tell whether to release body styles.
  private _wrappedMouseUpListener = (event: MouseEvent) => {
    document.body.removeEventListener('mouseup', this._wrappedMouseUpListener);
    document.body.removeEventListener(
      'mousemove',
      this._splitterMouseMoveHandler
    );
    this._suppressNextSplitterClick = this._splitterDragMoved;
    this._splitterDragInProgress = false;
    this.removeAttribute('resizing');
    fMouseUpListener(event);
    this.setSidebarWidth(event.clientX);
  };

  disconnectedCallback() {
    super.disconnectedCallback();
    this._narrowMq?.removeEventListener('change', this._narrowMqHandler);
    window.removeEventListener(
      LOCATION_CHANGED_EVENT,
      this._routerLocationChanged
    );
    document.removeEventListener('keydown', this._keydownHandler);
    if (this.splitter) {
      this.splitter.removeEventListener(
        'mousedown',
        this._splitterMouseDownHandler
      );
    }
    // Only release body styles we own, so coexisting modals/drags aren't clobbered.
    if (this._splitterDragInProgress) {
      document.body.removeEventListener(
        'mousemove',
        this._splitterMouseMoveHandler
      );
      document.body.removeEventListener(
        'mouseup',
        this._wrappedMouseUpListener
      );
      document.body.style.removeProperty('user-select');
      this._splitterDragInProgress = false;
      this.removeAttribute('resizing');
    }
    if (this._drawerLockedScroll) {
      // Restore the snapshot rather than blanket-removing, so a coexisting
      // modal's body-overflow lock isn't clobbered if we're torn down while
      // the drawer is open (mirrors _closeDrawer).
      if (this._previousBodyOverflow) {
        document.body.style.overflow = this._previousBodyOverflow;
      } else {
        document.body.style.removeProperty('overflow');
      }
      this._previousBodyOverflow = '';
      this._drawerLockedScroll = false;
    }
  }

  // The skip link's href cannot resolve across the shadow boundary, and letting
  // the browser act on it would push a #fragment into the SPA's URL. Move focus
  // directly instead — #page-content carries tabindex="-1" to receive it.
  // Reflected into aria-valuenow so AT can read the current drawer width.
  @state() private _sidebarWidth = 300;

  private static readonly WIDTH_MIN = 200;
  private static readonly WIDTH_MAX = 1000;
  // Preset widths for the non-dragging pointer alternative (WCAG 2.5.7).
  private static readonly WIDTH_PRESETS = [200, 300, 500];

  private setSidebarWidth(width: number) {
    const clamped = Math.max(
      DorcApp.WIDTH_MIN,
      Math.min(DorcApp.WIDTH_MAX, Math.round(width))
    );
    this._sidebarWidth = clamped;
    if (this.dorcNavbar) this.dorcNavbar.style.width = `${clamped}px`;
    if (!this._narrowScreen && !this._desktopSidebarVisible) {
      this._desktopSidebarVisible = true;
      this._applyDrawerAria();
    }
  }

  /**
   * Keyboard resize (WCAG 2.1.1). The splitter was mousedown/mousemove only, so
   * a keyboard user could never change the drawer width at all.
   */
  private onSplitterKeydown(e: KeyboardEvent) {
    const step = e.shiftKey ? 50 : 10;
    let next: number | null = null;

    if (e.key === 'ArrowLeft') next = this._sidebarWidth - step;
    else if (e.key === 'ArrowRight') next = this._sidebarWidth + step;
    else if (e.key === 'Home') next = DorcApp.WIDTH_MIN;
    else if (e.key === 'End') next = DorcApp.WIDTH_MAX;
    else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      this.cycleSidebarWidth();
      return;
    }

    if (next !== null) {
      e.preventDefault();
      this.setSidebarWidth(next);
    }
  }

  /**
   * Single-pointer, non-dragging alternative (WCAG 2.5.7, new in 2.2). Dragging
   * is not operable for head-pointer, eye-gaze or tremor users, and a keyboard
   * path does not discharge 2.5.7 — it requires a *pointer* alternative. Clicking
   * the separator cycles preset widths.
   */
  private cycleSidebarWidth() {
    if (this._splitterDragInProgress) return;
    if (this._suppressNextSplitterClick) {
      this._suppressNextSplitterClick = false;
      return;
    }
    const presets = DorcApp.WIDTH_PRESETS;
    const next = presets.find(w => w > this._sidebarWidth + 1) ?? presets[0];
    this.setSidebarWidth(next);
  }

  // ── Shortcut event hub ───────────────────────────────────────────────────
  // Absorbed from the former ShortcutsStore base class, which was both a
  // registered custom element that was never used as one AND the superclass of
  // this component, while also owning shortcut state. State now lives in
  // drawer-shortcuts; this is only the wiring between page events and the store.

  private registerShortcutEvents() {
    this.addEventListener(
      'open-env-detail',
      this.openEnvDetail as EventListener
    );
    this.addEventListener(
      'open-monitor-result',
      this.openMonitorResult as EventListener
    );
    this.addEventListener(
      'open-project-envs',
      this.openProjectEnvs as EventListener
    );
    this.addEventListener(
      'open-project-components',
      this.openProjectComponents as EventListener
    );
    this.addEventListener(
      'open-project-ref-data',
      this.openProjectRefData as EventListener
    );
    this.addEventListener(
      'environment-deleted',
      this.environmentDeleted as EventListener
    );
    this.addEventListener(
      'environment-renamed',
      this.environmentRenamed as EventListener
    );
    this.addEventListener(
      'environment-not-found',
      this.environmentNotFound as EventListener
    );
  }

  /**
   * SC-27: prunes a shortcut whose target no longer exists.
   *
   * `environment-deleted` only fires in the window that performed the delete, so
   * a shortcut for an environment removed or renamed by someone else used to
   * point at a 404 forever. Under cookies that self-corrected via the accidental
   * 7-day expiry; localStorage has none, so this is now the only thing that
   * prunes them.
   */
  private environmentNotFound = (e: CustomEvent) => {
    if (!e.detail?.confirmedNotFound) return;
    const segments = window.location.pathname.split('/');
    if (segments[1] !== 'environment' || !segments[2]) return;
    let name: string;
    try {
      name = decodeURIComponent(segments[2]);
    } catch {
      name = segments[2];
    }
    drawerShortcuts.removeEnvironmentByName(name);
  };

  private environmentDeleted = (e: CustomEvent) => {
    this.dorcNavbar?.closeEnvDetail(e);
    void navigate('/environments');
  };

  private environmentRenamed = (e: CustomEvent) => {
    this.dorcNavbar?.renameEnvDetail(e);
  };

  private openEnvDetail = (e: CustomEvent) => {
    const env = e.detail.Environment as EnvironmentApiModel;
    const tab = (e.detail.Tab as EnvPageTabNames) ?? EnvPageTabNames.Metadata;
    // Projection happens in the store, so the ~20 dispatch sites keep sending
    // full API models and their objects are never mutated.
    drawerShortcuts.add('environments', toEnvShortcut(env));
    void navigate(
      `/environment/${encodeURIComponent(String(env.EnvironmentName))}/${tab}`
    );
  };

  private openProjectEnvs = (e: CustomEvent) => {
    const project = e.detail.Project as ProjectApiModel;
    drawerShortcuts.add('projects', toProjectShortcut(project));
    void navigate(
      `/project-envs/${encodeURIComponent(String(project.ProjectName))}`
    );
  };

  private openMonitorResult = (e: CustomEvent) => {
    const request = e.detail.request as DeploymentRequestApiModel;
    drawerShortcuts.add('results', toResultShortcut(request));
    // Opens in another window, so the drawer selection in THIS one is left alone.
    window.open(`/monitor-result/${Number(request.Id)}`);
  };

  private openProjectComponents = (e: CustomEvent) => {
    const project = e.detail.Project as ProjectApiModel;
    void navigate(`/project-components/${project?.ProjectId}`);
  };

  private openProjectRefData = (e: CustomEvent) => {
    const project = e.detail.Project as ProjectApiModel;
    void navigate(`/project-ref-data/${project?.ProjectId}`);
  };

  /** Clears shortcuts on sign-out (SC-17). */
  public static clearShortcuts() {
    drawerShortcuts.clear();
  }

  private skipToContent(e: Event) {
    e.preventDefault();
    const content = this.shadowRoot?.getElementById('page-content');
    content?.focus();
    content?.scrollIntoView();
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
        getComputedStyle(this)
          .getPropertyValue('--dorc-sidebar-width')
          .trim() || '300px';
      if (this.dorcNavbar.style.width === '0px') {
        this.dorcNavbar.style.width = sidebarWidth;
        this._desktopSidebarVisible = true;
      } else {
        this.dorcNavbar.style.width = '0px';
        this._desktopSidebarVisible = false;
      }
      // Collapsed/expanded changes what must be reachable (D-39).
      this._applyDrawerAria();
    }
  }

  private _openDrawer() {
    if (!this.dorcNavbar) return;
    this._drawerOpen = true;
    this.dorcNavbar.classList.add('open');
    if (this._narrowScreen) {
      this._previouslyFocused = this._activeFocusedElement();
      // Snapshot any prior inline overflow (e.g. set by a coexisting modal)
      // so we restore the SAME value on close — releasing unconditionally
      // would clobber another overlay's scroll lock.
      this._previousBodyOverflow = document.body.style.overflow;
      document.body.style.overflow = 'hidden';
      this._drawerLockedScroll = true;
      // Move focus into the drawer so AT users land inside the modal.
      this.dorcNavbar.tabIndex = -1;
      this._drawerSetTabindex = true;
      this.dorcNavbar.focus();
    }
    this._applyDrawerAria();
  }

  private _closeDrawer() {
    if (!this.dorcNavbar) return;
    this._drawerOpen = false;
    this.dorcNavbar.classList.remove('open');
    if (this._drawerLockedScroll) {
      if (this._previousBodyOverflow) {
        document.body.style.overflow = this._previousBodyOverflow;
      } else {
        document.body.style.removeProperty('overflow');
      }
      this._previousBodyOverflow = '';
      this._drawerLockedScroll = false;
    }
    if (this._drawerSetTabindex) {
      this.dorcNavbar.removeAttribute('tabindex');
      this._drawerSetTabindex = false;
    }
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
  // dialog when open. `inert` is also applied to #page-content while the modal
  // is open so screen-reader virtual cursors can't browse behind the drawer.
  // The header (which contains the hamburger close button, Sign Out, Help) is
  // intentionally NOT inerted — those are siblings the user must still reach
  // while the drawer is shown. The drawer's static role is `navigation`; the
  // modal-open state temporarily upgrades it to `dialog` so the hamburger's
  // `aria-controls` reference always points at a roled landmark.
  private _applyDrawerAria() {
    if (!this.dorcNavbar) return;
    const pageContent = this._pageContent;
    if (this._narrowScreen) {
      if (this._drawerOpen) {
        this.dorcNavbar.removeAttribute('inert');
        this.dorcNavbar.removeAttribute('aria-hidden');
        this.dorcNavbar.setAttribute('role', 'dialog');
        this.dorcNavbar.setAttribute('aria-modal', 'true');
        this.dorcNavbar.setAttribute('aria-label', 'Navigation');
        pageContent?.setAttribute('inert', '');
      } else {
        this.dorcNavbar.setAttribute('inert', '');
        this.dorcNavbar.setAttribute('aria-hidden', 'true');
        this.dorcNavbar.setAttribute('role', 'navigation');
        this.dorcNavbar.removeAttribute('aria-modal');
        this.dorcNavbar.setAttribute('aria-label', 'Primary');
        pageContent?.removeAttribute('inert');
      }
    } else {
      this.dorcNavbar.setAttribute('role', 'navigation');
      this.dorcNavbar.removeAttribute('aria-modal');
      this.dorcNavbar.setAttribute('aria-label', 'Primary');
      pageContent?.removeAttribute('inert');
      // D-39: collapsing the sidebar on desktop only sets width:0 inside an
      // overflow:hidden host — it does not remove anything from the tab order.
      // Every nav link and shortcut stayed focusable and AT-exposed inside a
      // zero-width clipped box, so a keyboard user tabbed through 20+ invisible
      // stops before reaching content. Mirror the mobile treatment when collapsed.
      if (this._desktopSidebarVisible) {
        this.dorcNavbar.removeAttribute('inert');
        this.dorcNavbar.removeAttribute('aria-hidden');
      } else {
        this.dorcNavbar.setAttribute('inert', '');
        this.dorcNavbar.setAttribute('aria-hidden', 'true');
      }
      // Scroll-lock ownership is released exclusively by _closeDrawer(), which
      // is always invoked before we reach the desktop state (via the toggle,
      // Escape, router navigation, or the breakpoint handler). No release here.
    }
  }

  private getUserRoles() {
    const api = new RefDataRolesApi(dorcApiConfiguration);
    api.refDataRolesGet().subscribe({
      next: (data: string[]) => {
        this.userRoles = data.join(' | ');
      },
      error: (err: string) => console.error(err)
    });
  }

  private getUserEmail() {
    const api = new MakeLikeProdApi(dorcApiConfiguration);
    api.makeLikeProdNotifyEmailAddressGet().subscribe({
      next: value => {
        this.userEmail = value;
      },
      error: (err: string) => console.error(err)
    });
  }

  private getDorcEnv() {
    const api = new MetadataApi(dorcApiConfiguration);
    api.metadataGet().subscribe({
      next: (data: string) => {
        const environmentName = dorcEnvironmentNameFromMetadata(data);
        if (environmentName === undefined) {
          console.error('Metadata API returned a non-string environment name.');
          this.dorcEnv = '';
          return;
        }
        this.dorcEnv = environmentName;
      },
      error: (err: string) => console.error(err)
    });
  }

  @property({ type: Boolean }) showSignOutButton =
    appConfig.authenticationScheme == OAUTH_SCHEME;

  private signOut() {
    oauthServiceContainer.service.signOut();
  }
}
