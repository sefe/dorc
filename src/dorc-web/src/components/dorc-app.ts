import { css, PropertyValues } from 'lit';
import { html } from 'lit/html.js';
import { customElement, property, query } from 'lit/decorators.js';
import '@vaadin/button';
import { MakeLikeProdApi, RefDataRolesApi } from '../apis/dorc-api';
import './dorc-navbar.ts';
import { DorcNavbar } from './dorc-navbar.ts';
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
        display: flex;
        flex-direction: column;
        height: 100vh;
        height: 100dvh;
        margin: 0;
        background: white;
        font-family: var(--lumo-font-family, Arial, sans-serif);
        overflow: hidden;
      }

      #header {
        height: var(--dorc-header-height, 50px);
        flex-shrink: 0;
        display: flex;
        align-items: center;
        background: #f5f6f8;
        color: #555;
        gap: var(--lumo-space-s);
        padding: 0 var(--lumo-space-s);
        box-sizing: border-box;
      }

      #header h2 {
        margin: 0;
        white-space: nowrap;
      }

      .header-spacer {
        flex: 1;
        min-width: var(--lumo-space-m);
      }

      .header-user-info {
        color: #555;
        font-size: var(--lumo-font-size-xs);
        line-height: var(--lumo-line-height-s);
        text-align: right;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        max-width: 300px;
      }

      .header-user-info .user-roles {
        color: #747f8d;
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
        background-color: #f5f6f8;
      }

      #page-content {
        background: white;
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
          background: var(--lumo-base-color, white);
          box-shadow: 2px 0 8px rgba(0, 0, 0, 0.15);
        }

        #dorcNavbar.open {
          width: 280px;
        }

        #splitter {
          display: none;
        }

        .header-user-info {
          display: none;
        }

        #header {
          padding: 0 var(--lumo-space-xs);
        }
      }
    `;
  }

  @property() userEmail = '';
  @property() userRoles = '';

  @query('#splitter') splitter!: HTMLDivElement;

  render() {
    return html`
      <div id="header">
        <vaadin-button
          theme="icon"
          aria-label="Toggle Menu"
          @click="${this.toggleSideBar}"
        >
          <vaadin-icon icon="lumo:menu"></vaadin-icon>
        </vaadin-button>
        <img
          src="/hegsie_white_background_cartoon_dork_code_markdown_simple_icon__ef4f70a2-200b-4a67-82ba-73b12eb495d3.png"
          style="height: 40px"
          alt="DOrc mascot"
        />
        <h2 style="color: black" title="DevOps Orchestrator">
          DOrc
        </h2>

        <div class="header-spacer"></div>
        <div class="header-user-info">
          <div>${this.userEmail}</div>
          <div class="user-roles">${this.userRoles}</div>
        </div>
        <vaadin-button ?hidden="${!this.showSignOutButton}" @click="${this.signOut}">Sign Out</vaadin-button>
        <a
          class="plain"
          href="${this.dorcHelperPage}"
          target="_blank"
        >
          <vaadin-icon icon="vaadin:info-circle"></vaadin-icon>
          Help
        </a>
      </div>

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
    this.getUserEmail();
    this.getUserRoles();
    this.dorcHelperPage = appConfig.dorcHelperPage;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    // Assign dorcNavbar BEFORE calling super to ensure it's available for event handlers
    this.dorcNavbar = this.shadowRoot?.getElementById(
      'dorcNavbar'
    ) as DorcNavbar;
    dorcNavbar = this.dorcNavbar;

    super.firstUpdated(_changedProperties);

    // Auto-close drawer on mobile after navigation click
    const navbar = this.dorcNavbar;
    navbar.addEventListener('click', (e: Event) => {
      const isMobile = window.matchMedia('(max-width: 768px)').matches;
      if (isMobile && e.composedPath().some(el => (el as HTMLElement).tagName === 'A')) {
        navbar.classList.remove('open');
      }
    });

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
      const isMobile = window.matchMedia('(max-width: 768px)').matches;
      if (isMobile) {
        this.dorcNavbar.classList.toggle('open');
      } else {
        if (this.dorcNavbar.style.width === '0px') {
          this.dorcNavbar.style.width = '300px';
        } else {
          this.dorcNavbar.style.width = '0px';
        }
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

  @property({ type: Boolean }) showSignOutButton = appConfig.authenticationScheme == OAUTH_SCHEME;

  private signOut() {
    oauthServiceContainer.service.signOut();
  }
}