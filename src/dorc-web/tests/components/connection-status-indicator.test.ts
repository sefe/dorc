import { describe, it } from 'vitest';
import { expect, fixture, html } from '../_helpers';
import { ConnectionStatusIndicator } from '../../src/components/connection-status-indicator';
import { HubConnectionState } from '@microsoft/signalr';

function strip(el: ConnectionStatusIndicator): HTMLElement {
  return el.shadowRoot!.querySelector('.strip') as HTMLElement;
}

describe('ConnectionStatusIndicator', () => {
  it('shows pulsing Live strip briefly when connected and then collapses to a 3px animated green strip', async () => {
    const prevDelay = ConnectionStatusIndicator.LIVE_COLLAPSE_DELAY_MS;
    ConnectionStatusIndicator.LIVE_COLLAPSE_DELAY_MS = 40;
    try {
      const el = await fixture<ConnectionStatusIndicator>(html`
        <connection-status-indicator
          .state="${HubConnectionState.Connected}"
          .autoRefresh="${true}"
        ></connection-status-indicator>
      `);

      expect(el.shadowRoot!.querySelector('.pill')).to.be.null;
      const bar = strip(el);
      expect(bar.textContent).to.contain('Live');
      expect(bar.classList.contains('live')).to.be.true;
      expect(bar.classList.contains('expanded')).to.be.true;
      expect(bar.classList.contains('pulse')).to.be.true;

      await new Promise(resolve => setTimeout(resolve, 60));
      await el.updateComplete;

      expect(bar.classList.contains('collapsed')).to.be.true;
      expect(bar.classList.contains('expanded')).to.be.false;
      const content = el.shadowRoot!.querySelector(
        '.strip-content'
      ) as HTMLElement;
      expect(getComputedStyle(content).pointerEvents).to.equal('none');
    } finally {
      ConnectionStatusIndicator.LIVE_COLLAPSE_DELAY_MS = prevDelay;
    }
  });

  it('expands to show Paused status and Resume/Refresh buttons when connected with auto refresh off', async () => {
    const el = await fixture<ConnectionStatusIndicator>(html`
      <connection-status-indicator
        .state="${HubConnectionState.Connected}"
        .autoRefresh="${false}"
      ></connection-status-indicator>
    `);

    const bar = strip(el);
    expect(bar.textContent).to.contain('Paused');
    expect(bar.classList.contains('paused')).to.be.true;
    expect(bar.classList.contains('expanded')).to.be.true;
    expect(bar.classList.contains('pulse')).to.be.false;
    expect(
      el.shadowRoot!.querySelector('.strip-toggle')?.textContent
    ).to.contain('Resume');
  });

  it('expands to show Reconnecting status with pulse while the hub is reconnecting', async () => {
    const el = await fixture<ConnectionStatusIndicator>(html`
      <connection-status-indicator
        .state="${HubConnectionState.Reconnecting}"
        .autoRefresh="${true}"
      ></connection-status-indicator>
    `);

    const bar = strip(el);
    expect(bar.textContent).to.contain('Reconnecting');
    expect(bar.classList.contains('reconnecting')).to.be.true;
    expect(bar.classList.contains('expanded')).to.be.true;
    expect(bar.classList.contains('pulse')).to.be.true;
  });

  it('expands to show Offline status and clickable Pause and Refresh buttons when disconnected', async () => {
    const el = await fixture<ConnectionStatusIndicator>(html`
      <connection-status-indicator
        .state="${HubConnectionState.Disconnected}"
        .autoRefresh="${true}"
      ></connection-status-indicator>
    `);

    const bar = strip(el);
    expect(bar.classList.contains('expanded')).to.be.true;
    expect(bar.classList.contains('offline')).to.be.true;
    expect(bar.classList.contains('pulse')).to.be.false;
    expect(bar.textContent).to.contain('Offline');
    expect(bar.textContent).to.contain('Connection lost');

    const toggleBtn = el.shadowRoot!.querySelector(
      '.strip-toggle'
    ) as HTMLButtonElement | null;
    const refreshBtn = el.shadowRoot!.querySelector(
      'button[aria-label="Manual refresh"]'
    ) as HTMLButtonElement | null;
    expect(toggleBtn).to.not.be.null;
    expect(refreshBtn).to.not.be.null;

    let refreshed = false;
    el.addEventListener('refresh-requests', () => {
      refreshed = true;
    });
    refreshBtn!.click();
    expect(refreshed).to.be.true;
  });

  it('expands when the 3px collapsed live strip is clicked and collapses again after a moment of inactivity', async () => {
    const prevDelay = ConnectionStatusIndicator.LIVE_COLLAPSE_DELAY_MS;
    ConnectionStatusIndicator.LIVE_COLLAPSE_DELAY_MS = 40;
    try {
      const el = await fixture<ConnectionStatusIndicator>(html`
        <connection-status-indicator
          .state="${HubConnectionState.Connected}"
          .autoRefresh="${true}"
        ></connection-status-indicator>
      `);

      const bar = strip(el);
      await new Promise(resolve => setTimeout(resolve, 60));
      await el.updateComplete;
      expect(bar.classList.contains('collapsed')).to.be.true;

      const trigger = el.shadowRoot!.querySelector(
        '.strip-live-trigger'
      ) as HTMLButtonElement;
      expect(trigger).to.not.be.null;
      trigger.click();
      await el.updateComplete;

      expect(bar.classList.contains('expanded')).to.be.true;
      expect(bar.classList.contains('collapsed')).to.be.false;

      let toggled = false;
      el.addEventListener('toggle-auto-refresh', () => {
        toggled = true;
      });
      (
        el.shadowRoot!.querySelector('.strip-toggle') as HTMLButtonElement
      ).click();
      expect(toggled).to.be.true;

      await new Promise(resolve => setTimeout(resolve, 60));
      await el.updateComplete;
      expect(bar.classList.contains('collapsed')).to.be.true;
    } finally {
      ConnectionStatusIndicator.LIVE_COLLAPSE_DELAY_MS = prevDelay;
    }
  });

  it('does not promise live updates in the tooltip while offline', async () => {
    const el = await fixture<ConnectionStatusIndicator>(html`
      <connection-status-indicator
        .state="${HubConnectionState.Disconnected}"
        .autoRefresh="${true}"
      ></connection-status-indicator>
    `);

    const title = (
      el.shadowRoot!.querySelector('.strip-toggle') as HTMLButtonElement
    ).title;
    expect(title).to.contain('unavailable');
    expect(title).to.not.contain('click to resume');
  });

  it('shows Paused, not Reconnecting, when paused during reconnection', async () => {
    const el = await fixture<ConnectionStatusIndicator>(html`
      <connection-status-indicator
        .state="${HubConnectionState.Reconnecting}"
        .autoRefresh="${false}"
      ></connection-status-indicator>
    `);

    const bar = strip(el);
    expect(bar.textContent).to.contain('Paused');
    expect(bar.classList.contains('paused')).to.be.true;
    const toggleBtn = el.shadowRoot!.querySelector(
      '.strip-toggle'
    ) as HTMLButtonElement;
    expect(toggleBtn.title).to.contain('click to resume');
  });

  it('shows Paused, not Offline, when paused while disconnected', async () => {
    const el = await fixture<ConnectionStatusIndicator>(html`
      <connection-status-indicator
        .state="${HubConnectionState.Disconnected}"
        .autoRefresh="${false}"
      ></connection-status-indicator>
    `);

    const bar = strip(el);
    expect(bar.textContent).to.contain('Paused');
    expect(bar.classList.contains('paused')).to.be.true;
  });

  it('reflects auto refresh state via aria-pressed', async () => {
    const el = await fixture<ConnectionStatusIndicator>(html`
      <connection-status-indicator
        .state="${HubConnectionState.Connected}"
        .autoRefresh="${false}"
      ></connection-status-indicator>
    `);

    expect(
      el
        .shadowRoot!.querySelector('.strip-toggle')!
        .getAttribute('aria-pressed')
    ).to.equal('false');
  });
});
