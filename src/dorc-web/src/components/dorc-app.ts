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
        display: inline;
        height: 100%;
        margin: 0;
        background: black;
        font-family: Arial, monospace;
      }

      #header {
        height: 50px;
        display: flex;
        align-items: center;
        background: #f5f6f8;
        color: #bbbbbb;
      }

      #page {
        display: flex;
        height: calc(100vh - 30px);
        /* calculate the height. Header is 30px */
      }

      #sideBar {
        width: 300px;
        background: blue;
      }

      #splitter {
        width: 2px;
        min-width: 2px;
        cursor: ew-resize;
        padding: 4px 0 0;
        top: 0;
        right: 0;
        bottom: 0;
        background-color: #f5f6f8;
      }

      #page-content {
        background: white;
        overflow-x: scroll;
        overflow-y: hidden;
        width: 100%;
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
          style="padding: 5px; margin-left: 10px"
          @click="${this.toggleSideBar}"
        >
          <vaadin-icon icon="lumo:menu"></vaadin-icon>
        </vaadin-button>
        <img
          src="/hegsie_white_background_cartoon_dork_code_markdown_simple_icon__ef4f70a2-200b-4a67-82ba-73b12eb495d3.png"
          style="height: 65px; padding: 3px"
          alt="DOrc mascot"
        />
        <h2 style="padding: 5px;  color: black" title="Deployment Orchestrator">
          DOrc
        </h2>

        <div style="width: calc(100% - 800px)"></div>
        <table style="color: #747f8d; font-size: x-small">
          <tr>
            ${this.userEmail}
          </tr>
          <tr>
            ${this.userRoles}
          </tr>
        </table>
        <a
          class="plain"
          href="${this.dorcHelperPage}"
          target="_blank"
          style="padding-left: 10px"
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
    super.firstUpdated(_changedProperties);

    this.dorcNavbar = this.shadowRoot?.getElementById(
      'dorcNavbar'
    ) as DorcNavbar;
    dorcNavbar = this.dorcNavbar;

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
}
