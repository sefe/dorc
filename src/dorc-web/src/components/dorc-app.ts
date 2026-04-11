import { css, PropertyValues } from 'lit';
import { html } from 'lit/html.js';
import { customElement, property, query } from 'lit/decorators.js';
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

let dorcNavbar: DorcNavbar;

function fMouseMoveListener(event: MouseEvent) {
  const width = Math.max(200, Math.min(1000, event.clientX));
  const widthInPx = `${width}px`;

  requestAnimationFrame(() => {
    dorcNavbar.style.width = widthInPx;
  });
}
function fMouseUpListener(event: MouseEvent) {
  document.body.style.removeProperty('user-select');

  document.body.removeEventListener('mousemove', fMouseMoveListener);
  document.body.removeEventListener('mouseup', fMouseUpListener);

  const width = Math.max(200, Math.min(1000, event.clientX));
  dorcNavbar.style.width = width + 'px';
}

@customElement('dorc-app')
export class DorcApp extends ShortcutsStore {
  static get styles() {
    return css`
      :host {
        --header-height: 68px;
        display: inline;
        height: 100%;
        margin: 0;
        background: var(--dorc-bg-primary);
        font-family: Arial, monospace;
      }

      #header {
        height: var(--header-height);
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
        height: calc(var(--header-height) - 2px);
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
        height: calc(100vh - var(--header-height));
      }

      #splitter {
        width: 2px;
        min-width: 2px;
        cursor: ew-resize;
        padding: 4px 0 0;
        top: 0;
        right: 0;
        bottom: 0;
        background-color: var(--dorc-bg-secondary);
      }

      #page-content {
        background: var(--dorc-bg-primary);
        overflow-x: scroll;
        overflow-y: hidden;
        width: 100%;
      }
    `;
  }

  @property() userEmail = '';
  @property() userRoles = '';
  @property() dorcEnv = '';

  @query('#splitter') splitter!: HTMLDivElement;

  render() {
    return html`
      <header id="header" role="banner">
        <vaadin-button
          class="menu-btn"
          theme="icon"
          aria-label="Toggle Menu"
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

  protected firstUpdated(_changedProperties: PropertyValues) {
    // Assign dorcNavbar BEFORE calling super to ensure it's available for event handlers
    this.dorcNavbar = this.shadowRoot?.getElementById(
      'dorcNavbar'
    ) as DorcNavbar;
    dorcNavbar = this.dorcNavbar;

    super.firstUpdated(_changedProperties);

    this.splitter.addEventListener('mousedown', () => {
      document.body.addEventListener('mousemove', fMouseMoveListener, {
        passive: true
      });
      document.body.addEventListener('mouseup', fMouseUpListener);

      document.body.style.setProperty('user-select', 'none');
    });
  }

  private toggleSideBar() {
    if (this.dorcNavbar) {
      if (this.dorcNavbar.style.width === '0px') {
        this.dorcNavbar.style.width = '300px';
      } else {
        this.dorcNavbar.style.width = '0px';
      }
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