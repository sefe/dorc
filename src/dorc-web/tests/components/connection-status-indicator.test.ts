import { describe, it } from 'vitest';
import { expect, fixture, html } from '../_helpers';
import '../../src/components/connection-status-indicator';
import type { ConnectionStatusIndicator } from '../../src/components/connection-status-indicator';
import { HubConnectionState } from '@microsoft/signalr';

function pill(el: ConnectionStatusIndicator): HTMLElement | null {
  return el.shadowRoot!.querySelector('.pill');
}

describe('ConnectionStatusIndicator', () => {
  describe('toggle mode', () => {
    it('shows a pulsing Live pill when connected with auto refresh on', async () => {
      const el = await fixture<ConnectionStatusIndicator>(html`
        <connection-status-indicator
          mode="toggle"
          .state="${HubConnectionState.Connected}"
          .autoRefresh="${true}"
        ></connection-status-indicator>
      `);

      const badge = pill(el)!;
      expect(badge.textContent).to.contain('Live');
      expect(badge.classList.contains('live')).to.be.true;
      expect(badge.classList.contains('pulse')).to.be.true;
      expect(badge.tagName).to.equal('BUTTON');
    });

    it('shows a Paused pill when connected with auto refresh off', async () => {
      const el = await fixture<ConnectionStatusIndicator>(html`
        <connection-status-indicator
          mode="toggle"
          .state="${HubConnectionState.Connected}"
          .autoRefresh="${false}"
        ></connection-status-indicator>
      `);

      const badge = pill(el)!;
      expect(badge.textContent).to.contain('Paused');
      expect(badge.classList.contains('paused')).to.be.true;
      expect(badge.classList.contains('pulse')).to.be.false;
    });

    it('shows a Reconnecting pill while the hub is reconnecting', async () => {
      const el = await fixture<ConnectionStatusIndicator>(html`
        <connection-status-indicator
          mode="toggle"
          .state="${HubConnectionState.Reconnecting}"
          .autoRefresh="${true}"
        ></connection-status-indicator>
      `);

      const badge = pill(el)!;
      expect(badge.textContent).to.contain('Reconnecting');
      expect(badge.classList.contains('reconnecting')).to.be.true;
      expect(badge.classList.contains('pulse')).to.be.true;
    });

    it('shows an Offline pill when disconnected', async () => {
      const el = await fixture<ConnectionStatusIndicator>(html`
        <connection-status-indicator
          mode="toggle"
          .state="${HubConnectionState.Disconnected}"
          .autoRefresh="${true}"
        ></connection-status-indicator>
      `);

      const badge = pill(el)!;
      expect(badge.textContent).to.contain('Offline');
      expect(badge.classList.contains('offline')).to.be.true;
      expect(badge.classList.contains('pulse')).to.be.false;
    });

    it('emits toggle-auto-refresh when clicked', async () => {
      const el = await fixture<ConnectionStatusIndicator>(html`
        <connection-status-indicator
          mode="toggle"
          .state="${HubConnectionState.Connected}"
          .autoRefresh="${true}"
        ></connection-status-indicator>
      `);

      let toggled = false;
      el.addEventListener('toggle-auto-refresh', () => {
        toggled = true;
      });
      (pill(el) as HTMLButtonElement).click();

      expect(toggled).to.be.true;
    });

    it('reflects auto refresh state via aria-pressed', async () => {
      const el = await fixture<ConnectionStatusIndicator>(html`
        <connection-status-indicator
          mode="toggle"
          .state="${HubConnectionState.Connected}"
          .autoRefresh="${false}"
        ></connection-status-indicator>
      `);

      expect(pill(el)!.getAttribute('aria-pressed')).to.equal('false');
    });
  });

  describe('icon (passive) mode', () => {
    it('renders nothing when connected by default', async () => {
      const el = await fixture<ConnectionStatusIndicator>(html`
        <connection-status-indicator
          mode="icon"
          .state="${HubConnectionState.Connected}"
        ></connection-status-indicator>
      `);

      expect(pill(el)).to.be.null;
    });

    it('shows a Live pill when connected and showWhenConnected is set', async () => {
      const el = await fixture<ConnectionStatusIndicator>(html`
        <connection-status-indicator
          mode="icon"
          .state="${HubConnectionState.Connected}"
          .showWhenConnected="${true}"
        ></connection-status-indicator>
      `);

      const badge = pill(el)!;
      expect(badge.textContent).to.contain('Live');
      expect(badge.tagName).to.equal('SPAN');
      expect(badge.getAttribute('role')).to.equal('status');
    });

    it('shows an Offline pill when disconnected even without showWhenConnected', async () => {
      const el = await fixture<ConnectionStatusIndicator>(html`
        <connection-status-indicator
          mode="icon"
          .state="${HubConnectionState.Disconnected}"
        ></connection-status-indicator>
      `);

      expect(pill(el)!.textContent).to.contain('Offline');
    });
  });
});
