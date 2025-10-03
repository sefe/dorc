import { LitElement, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/button';
import '@vaadin/icon';
import '../icons/custom-icons.js';
import { HubConnectionState } from '@microsoft/signalr';

/**
 * Connection Status Indicator
 * Modes:
 *  - toggle: renders a button that toggles auto refresh (emits 'toggle-auto-refresh')
 *  - icon: renders a passive status icon only when state !== HubConnectionState.Connected
 */
@customElement('connection-status-indicator')
export class ConnectionStatusIndicator extends LitElement {
  @property({ type: String }) state: string | undefined = HubConnectionState.Disconnected;
  @property({ type: Boolean }) autoRefresh: boolean = false;
  @property({ type: String }) mode: 'toggle' | 'icon' = 'icon';
  /** When mode=icon, show icon even if Connected */
  @property({ type: Boolean }) showWhenConnected: boolean = false;

  static styles = css`
    :host { display: inline-flex; }
    vaadin-button { padding:0; margin:0; }
    vaadin-icon { width: var(--lumo-icon-size-m); height: var(--lumo-icon-size-m); }
  `;

  private get iconName() {
    if (this.mode === 'toggle') {
      return this.autoRefresh ? 'custom:refresh-auto' : 'custom:refresh-auto-off';
    }
    // passive mode: only one icon variant - reuse off icon when not connected
    if (this.state !== HubConnectionState.Connected) return 'custom:refresh-auto-off';
    return 'custom:refresh-auto';
  }

  private get iconColor() {
    if (this.mode === 'toggle') {
      if (this.state !== HubConnectionState.Connected && this.autoRefresh) return 'var(--lumo-error-color)';
      return 'cornflowerblue';
    }
    if (this.state !== HubConnectionState.Connected) return 'var(--lumo-error-color)';
    return 'cornflowerblue';
  }

  private get titleText() {
    if (this.mode === 'toggle') {
      return this.autoRefresh
        ? `Auto refresh ON (click to switch to manual)\nState: ${this.state}`
        : 'Manual mode (click to enable auto refresh)';
    }
    return this.state;
  }

  private toggle() {
    this.dispatchEvent(new CustomEvent('toggle-auto-refresh', { bubbles: true, composed: true }));
  }

  render() {
    if (this.mode === 'toggle') {
      return html`
        <vaadin-button
          theme="icon small tertiary-inline"
          style="padding:0;margin:0"
          .title="${this.titleText}"
          @click="${this.toggle}"
        >
          <vaadin-icon icon="${this.iconName}" style="color:${this.iconColor}"></vaadin-icon>
        </vaadin-button>
      `;
    }
    if (!this.showWhenConnected && this.state === HubConnectionState.Connected) return html``;
    return html`
      <vaadin-icon
        icon="${this.iconName}"
        .title="${this.titleText}"
        aria-label="${this.state}"
        style="color:${this.iconColor}; margin:4px;"
      ></vaadin-icon>
    `;
  }
}

declare global { interface HTMLElementTagNameMap { 'connection-status-indicator': ConnectionStatusIndicator; } }
