import { LitElement, PropertyValues, css, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { HubConnectionState } from '@microsoft/signalr';
import '@vaadin/tooltip';

type IndicatorStatus = 'live' | 'paused' | 'reconnecting' | 'offline';

/**
 * Connection Status Indicator
 *
 * Modern full-width status strip reflecting the SignalR hub state:
 *  - Live         connected (auto refresh on): briefly shows "Live · Real-time updates active"
 *                 with a pulsing green dot, then collapses into a 3px subtly animated green strip
 *  - Paused       auto refresh switched off: expands to show status + Resume & Refresh buttons
 *  - Reconnecting connecting/reconnecting: expands with pulsing amber dot + Pause & Refresh buttons
 *  - Offline      disconnected or errored: expands with red dot + Pause & Refresh buttons
 *
 * Events emitted:
 *  - 'toggle-auto-refresh' when the strip or Pause/Resume button is clicked
 *  - 'refresh-requests' when the manual Refresh button is clicked
 */
@customElement('connection-status-indicator')
export class ConnectionStatusIndicator extends LitElement {
  static LIVE_COLLAPSE_DELAY_MS = 2200;

  @property({ type: String }) state: string | undefined =
    HubConnectionState.Disconnected;
  @property({ type: Boolean }) autoRefresh: boolean = true;

  @state() private liveExpanded: boolean = false;
  private prevStatus?: IndicatorStatus;
  private collapseTimer?: ReturnType<typeof setTimeout>;

  static styles = css`
    :host {
      display: flex;
      width: 100%;
      flex-shrink: 0;
    }

    .strip {
      position: relative;
      display: flex;
      align-items: center;
      gap: 12px;
      width: 100%;
      height: 28px;
      padding: 0 12px;
      box-sizing: border-box;
      overflow: hidden;
      font-family: var(--lumo-font-family, sans-serif);
      font-size: 0.72rem;
      font-weight: 500;
      line-height: 1;
      letter-spacing: 0.015em;
      white-space: nowrap;
      user-select: none;
      border-bottom: 1px solid color-mix(in srgb, currentColor 22%, transparent);
      transition:
        height 0.4s cubic-bezier(0.22, 1, 0.36, 1),
        background-color 0.3s ease,
        border-color 0.3s ease;
    }

    .strip-content {
      display: inline-flex;
      align-items: center;
      gap: 12px;
      opacity: 1;
      transform: translateY(0);
      transition:
        opacity 0.2s ease,
        transform 0.25s cubic-bezier(0.22, 1, 0.36, 1);
    }

    .strip.live.collapsed {
      height: 3px;
      padding: 0;
      border-bottom-color: transparent;
      background: linear-gradient(
        90deg,
        color-mix(in srgb, var(--dorc-success-text, #2ea043) 55%, transparent)
          0%,
        var(--dorc-success-text, #3fb950) 50%,
        color-mix(in srgb, var(--dorc-success-text, #2ea043) 55%, transparent)
          100%
      );
      background-color: color-mix(
        in srgb,
        var(--dorc-success-text, #2ea043) 35%,
        var(--dorc-bg-secondary, #1e222a)
      );
      background-size: 200% 100%;
      animation: csi-strip-flow 6s ease-in-out infinite;
    }

    .strip.live.collapsed .strip-content {
      opacity: 0;
      transform: translateY(-4px);
      pointer-events: none;
    }

    .strip-live-trigger {
      position: absolute;
      inset: 0;
      width: 100%;
      height: 100%;
      padding: 0;
      margin: 0;
      border: 0;
      background: transparent;
      cursor: pointer;
    }

    .strip.live.collapsed:hover {
      filter: brightness(1.18);
    }

    @keyframes csi-strip-flow {
      0% {
        background-position: 100% 0;
      }
      50% {
        background-position: 0% 0;
      }
      100% {
        background-position: -100% 0;
      }
    }

    .strip.expanded.live {
      color: var(--dorc-success-text, #2ea043);
      background: color-mix(
        in srgb,
        var(--dorc-success-text, #2ea043) 14%,
        var(--dorc-bg-secondary, #f5f6f8)
      );
    }

    .strip.expanded.paused {
      color: var(--dorc-text-secondary, #8b949e);
      background: color-mix(
        in srgb,
        var(--dorc-text-secondary, #8b949e) 12%,
        var(--dorc-bg-secondary, #f5f6f8)
      );
    }

    .strip.expanded.reconnecting {
      color: var(--dorc-warning-text, #d29922);
      background: color-mix(
        in srgb,
        var(--dorc-warning-text, #d29922) 14%,
        var(--dorc-bg-secondary, #f5f6f8)
      );
    }

    .strip.expanded.offline {
      color: var(--dorc-error-color, #f85149);
      background: color-mix(
        in srgb,
        var(--dorc-error-color, #f85149) 14%,
        var(--dorc-bg-secondary, #f5f6f8)
      );
    }

    .strip-status {
      display: inline-flex;
      align-items: center;
      gap: 7px;
      font-weight: 600;
    }

    .strip-subtext {
      font-weight: 400;
      opacity: 0.85;
    }

    .strip-actions {
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }

    .strip-btn {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      height: 19px;
      padding: 0 8px;
      margin: 0;
      border-radius: 4px;
      border: 1px solid color-mix(in srgb, currentColor 32%, transparent);
      background: color-mix(in srgb, currentColor 10%, transparent);
      color: inherit;
      font-family: inherit;
      font-size: 0.6875rem;
      font-weight: 600;
      letter-spacing: 0.01em;
      cursor: pointer;
      transition:
        background-color 0.15s ease,
        border-color 0.15s ease;
    }

    .strip-btn:hover {
      background: color-mix(in srgb, currentColor 20%, transparent);
      border-color: color-mix(in srgb, currentColor 48%, transparent);
    }

    .strip-btn:active {
      background: color-mix(in srgb, currentColor 26%, transparent);
    }

    .strip-btn svg {
      width: 11px;
      height: 11px;
      fill: none;
      stroke: currentColor;
      stroke-width: 2;
      stroke-linecap: round;
      stroke-linejoin: round;
      flex-shrink: 0;
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
      .strip.live.collapsed {
        animation: none;
      }
    }
  `;

  disconnectedCallback() {
    super.disconnectedCallback();
    this.clearCollapseTimer();
  }

  protected willUpdate(changedProperties: PropertyValues<this>) {
    if (
      changedProperties.has('state') ||
      changedProperties.has('autoRefresh')
    ) {
      const currentStatus = this.status;
      if (currentStatus !== this.prevStatus) {
        this.clearCollapseTimer();
        if (currentStatus === 'live') {
          this.expandLive();
        } else {
          this.liveExpanded = false;
        }
        this.prevStatus = currentStatus;
      }
    }
  }

  private scheduleLiveCollapse() {
    this.clearCollapseTimer();
    this.collapseTimer = setTimeout(() => {
      this.liveExpanded = false;
    }, ConnectionStatusIndicator.LIVE_COLLAPSE_DELAY_MS);
  }

  private expandLive() {
    if (this.status !== 'live') return;
    this.liveExpanded = true;
    this.scheduleLiveCollapse();
  }

  private clearCollapseTimer() {
    if (this.collapseTimer !== undefined) {
      clearTimeout(this.collapseTimer);
      this.collapseTimer = undefined;
    }
  }

  private get status(): IndicatorStatus {
    // The user's explicit pause wins over connection churn: while paused the
    // connection is stopped deliberately, so don't show Reconnecting/Offline.
    if (!this.autoRefresh) return 'paused';
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

  private get stripDetail() {
    switch (this.status) {
      case 'live':
        return 'Real-time updates active';
      case 'paused':
        return 'Live updates paused';
      case 'reconnecting':
        return 'Attempting to reconnect…';
      default:
        return 'Connection lost — live updates unavailable';
    }
  }

  private get titleText() {
    const state = `Connection: ${this.state}`;
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

  private get stripClass() {
    if (this.status === 'live' && !this.liveExpanded) {
      return 'strip live collapsed';
    }
    const pulse = this.status === 'live' || this.status === 'reconnecting';
    return `strip expanded ${this.status}${pulse ? ' pulse' : ''}`;
  }

  private toggle() {
    this.dispatchEvent(
      new CustomEvent('toggle-auto-refresh', { bubbles: true, composed: true })
    );
  }

  private manualRefresh() {
    if (this.status === 'live' && this.liveExpanded) {
      this.scheduleLiveCollapse();
    }
    this.dispatchEvent(
      new CustomEvent('refresh-requests', {
        detail: {},
        bubbles: true,
        composed: true
      })
    );
  }

  render() {
    const isCollapsedLive = this.status === 'live' && !this.liveExpanded;
    return html`
      <div class="${this.stripClass}" role="region" aria-label="Live updates">
        <div class="strip-content" aria-hidden="${isCollapsedLive}">
          <span class="strip-status">
            <span class="dot"></span>
            <span>${this.label}</span>
            <span class="strip-subtext">· ${this.stripDetail}</span>
          </span>
          <span class="strip-actions">
            <button
              type="button"
              class="strip-btn strip-toggle"
              .title="${this.titleText}"
              aria-label="${this.label}"
              aria-pressed="${this.autoRefresh}"
              ?tabindex="${isCollapsedLive ? -1 : undefined}"
              @click="${this.toggle}"
            >
              ${
                this.autoRefresh
                  ? html`
                      <svg viewBox="0 0 16 16" aria-hidden="true">
                        <path d="M5.5 3.5v9M10.5 3.5v9" />
                      </svg>
                      <span>Pause</span>
                    `
                  : html`
                      <svg viewBox="0 0 16 16" aria-hidden="true">
                        <polygon
                          points="4.5,3 12.5,8 4.5,13"
                          fill="currentColor"
                          stroke="none"
                        />
                      </svg>
                      <span>Resume</span>
                    `
              }
            </button>
            <button
              type="button"
              class="strip-btn strip-refresh"
              aria-label="Manual refresh"
              title="Manual refresh"
              ?tabindex="${isCollapsedLive ? -1 : undefined}"
              @click="${this.manualRefresh}"
            >
              <svg viewBox="0 0 16 16" aria-hidden="true">
                <path d="M13.5 8a5.5 5.5 0 1 1-1.6-3.9M13.5 2.5v3h-3" />
              </svg>
              <span>Refresh</span>
            </button>
          </span>
        </div>
        ${
          isCollapsedLive
            ? html`
                <button
                  type="button"
                  class="strip-live-trigger"
                  title="Live updates active — click to expand controls"
                  aria-label="Expand live status controls"
                  aria-expanded="false"
                  @click="${this.expandLive}"
                ></button>
              `
            : nothing
        }
      </div>
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'connection-status-indicator': ConnectionStatusIndicator;
  }
}
