import { LitElement, css, nothing } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { HubConnectionState } from '@microsoft/signalr';
import '@vaadin/tooltip';

type IndicatorStatus = 'live' | 'paused' | 'reconnecting' | 'offline';

/**
 * Connection Status Indicator
 *
 * Compact pill badge with a status dot reflecting the SignalR hub state:
 *  - Live         connected (auto refresh on in toggle mode), pulsing green dot
 *  - Paused       auto refresh switched off (toggle mode only), grey dot
 *  - Reconnecting connecting/reconnecting, pulsing amber dot
 *  - Offline      disconnected or errored, red dot
 *
 * Modes:
 *  - toggle: clickable pill that toggles auto refresh (emits 'toggle-auto-refresh')
 *  - icon: passive pill; hidden while Connected unless showWhenConnected is set
 */
@customElement('connection-status-indicator')
export class ConnectionStatusIndicator extends LitElement {
  @property({ type: String }) state: string | undefined =
    HubConnectionState.Disconnected;
  @property({ type: Boolean }) autoRefresh: boolean = false;
  @property({ type: String }) mode: 'toggle' | 'icon' = 'icon';
  /** When mode=icon, show the pill even if Connected */
  @property({ type: Boolean }) showWhenConnected: boolean = false;

  static styles = css`
    :host {
      display: inline-flex;
    }

    .pill {
      display: inline-flex;
      align-items: center;
      gap: 5px;
      padding: 2px 8px;
      border-radius: 999px;
      border: 1px solid transparent;
      font-family: var(--lumo-font-family, sans-serif);
      font-size: var(--lumo-font-size-xxs, 0.75rem);
      font-weight: 500;
      line-height: 1.4;
      letter-spacing: 0.02em;
      white-space: nowrap;
      user-select: none;
      cursor: default;
    }

    button.pill {
      cursor: pointer;
    }

    button.pill:hover {
      filter: brightness(0.95);
    }

    .dot {
      position: relative;
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: currentColor;
      flex: none;
    }

    .pulse .dot::after {
      content: '';
      position: absolute;
      inset: 0;
      border-radius: 50%;
      background: currentColor;
      animation: csi-pulse 2s ease-out infinite;
    }

    @keyframes csi-pulse {
      0% {
        transform: scale(1);
        opacity: 0.6;
      }
      70%,
      100% {
        transform: scale(2.6);
        opacity: 0;
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .pulse .dot::after {
        animation: none;
        display: none;
      }
    }

    .live {
      color: var(--dorc-success-text, #1a7a2e);
      background: var(--dorc-success-bg, #b1ffb7);
      border-color: color-mix(in srgb, currentColor 25%, transparent);
    }

    .paused {
      color: var(--dorc-text-secondary, #747f8d);
      background: var(--dorc-bg-tertiary, #eee);
      border-color: color-mix(in srgb, currentColor 25%, transparent);
    }

    .reconnecting {
      color: var(--dorc-warning-text, #856404);
      background: var(--dorc-warning-bg, #fff3cd);
      border-color: color-mix(in srgb, currentColor 25%, transparent);
    }

    .offline {
      color: var(--dorc-error-color, #ff3131);
      background: var(--dorc-failure-bg, #ffd9d9);
      border-color: color-mix(in srgb, currentColor 25%, transparent);
    }
  `;

  private get status(): IndicatorStatus {
    // The user's explicit pause wins over connection churn: while paused the
    // connection is stopped deliberately, so don't show Reconnecting/Offline.
    if (this.mode === 'toggle' && !this.autoRefresh) return 'paused';
    if (
      this.state === HubConnectionState.Connecting ||
      this.state === HubConnectionState.Reconnecting
    ) {
      return 'reconnecting';
    }
    if (this.state !== HubConnectionState.Connected) return 'offline';
    return 'live';
  }

  private get label() {
    switch (this.status) {
      case 'live':
        return 'Live';
      case 'paused':
        return 'Paused';
      case 'reconnecting':
        return 'Reconnecting…';
      default:
        return 'Offline';
    }
  }

  private get titleText() {
    const state = `Connection: ${this.state}`;
    if (this.mode === 'toggle') {
      if (this.status === 'paused') {
        return `Live updates paused (click to resume)\n${state}`;
      }
      if (this.status === 'reconnecting') {
        return `Reconnecting - live updates will resume automatically\n${state}`;
      }
      if (this.status === 'offline') {
        return `Connection lost - live updates unavailable\n${state}`;
      }
      return `Live updates on (click to pause)\n${state}`;
    }
    return state;
  }

  private get pillClass() {
    const pulse = this.status === 'live' || this.status === 'reconnecting';
    return `pill ${this.status}${pulse ? ' pulse' : ''}`;
  }

  private toggle() {
    this.dispatchEvent(
      new CustomEvent('toggle-auto-refresh', { bubbles: true, composed: true })
    );
  }

  render() {
    if (this.mode === 'toggle') {
      return html`
        <button
          type="button"
          class="${this.pillClass}"
          .title="${this.titleText}"
          aria-pressed="${this.autoRefresh}"
          @click="${this.toggle}"
        >
          <span class="dot"></span><span>${this.label}</span>
        </button>
      `;
    }
    if (!this.showWhenConnected && this.state === HubConnectionState.Connected)
      return nothing;
    return html`
      <span
        class="${this.pillClass}"
        role="status"
        .title="${this.titleText}"
        aria-label="${this.label}"
      >
        <span class="dot"></span><span>${this.label}</span>
      </span>
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'connection-status-indicator': ConnectionStatusIndicator;
  }
}
