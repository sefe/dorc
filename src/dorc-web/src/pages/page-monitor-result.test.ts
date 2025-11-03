import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { fixture, html } from '@open-wc/testing-helpers';
import './page-monitor-result';
import type { PageMonitorResult } from './page-monitor-result';

// Mock the SignalR dependencies
vi.mock('../services/ServerEvents/DeploymentHub', () => ({
  DeploymentHub: {
    getConnection: vi.fn(() => ({
      state: 'Disconnected',
      start: vi.fn(() => Promise.resolve()),
      stop: vi.fn(() => Promise.resolve()),
      onclose: vi.fn(),
      onreconnecting: vi.fn(),
      onreconnected: vi.fn(),
    })),
  },
}));

vi.mock('../services/ServerEvents', () => ({
  getReceiverRegister: vi.fn(() => ({
    register: vi.fn(() => ({
      dispose: vi.fn(),
    })),
  })),
  getHubProxyFactory: vi.fn(() => ({
    createHubProxy: vi.fn(() => ({
      joinRequestGroup: vi.fn(() => Promise.resolve()),
      leaveRequestGroup: vi.fn(() => Promise.resolve()),
    })),
  })),
  HubConnectionState: {
    Disconnected: 'Disconnected',
    Connected: 'Connected',
  },
}));

describe('PageMonitorResult - SignalR Subscription Management', () => {
  let element: PageMonitorResult;

  beforeEach(async () => {
    // Clear all mocks before each test
    vi.clearAllMocks();
  });

  afterEach(() => {
    if (element && element.parentNode) {
      element.parentNode.removeChild(element);
    }
  });

  it('should store the SignalR subscription when initializing', async () => {
    const { getReceiverRegister } = await import('../services/ServerEvents');
    const mockDispose = vi.fn();
    const mockRegister = vi.fn(() => ({ dispose: mockDispose }));
    
    (getReceiverRegister as any).mockReturnValue({
      register: mockRegister,
    });

    element = await fixture(html`<page-monitor-result></page-monitor-result>`);
    
    // Wait for component to initialize
    await element.updateComplete;
    
    // Access private property for testing
    const subscription = (element as any).signalRSubscription;
    
    // Verify subscription was stored
    expect(subscription).toBeDefined();
    expect(typeof subscription?.dispose).toBe('function');
  });

  it('should dispose SignalR subscription when disconnected', async () => {
    const mockDispose = vi.fn();
    const mockRegister = vi.fn(() => ({ dispose: mockDispose }));
    
    const { getReceiverRegister } = await import('../services/ServerEvents');
    (getReceiverRegister as any).mockReturnValue({
      register: mockRegister,
    });

    element = await fixture(html`<page-monitor-result></page-monitor-result>`);
    await element.updateComplete;
    
    // Manually set a subscription to test disposal
    (element as any).signalRSubscription = { dispose: mockDispose };
    
    // Trigger disconnectedCallback
    element.remove();
    
    // Verify dispose was called
    expect(mockDispose).toHaveBeenCalledTimes(1);
  });

  it('should set subscription to undefined after disposal', async () => {
    const mockDispose = vi.fn();
    
    element = await fixture(html`<page-monitor-result></page-monitor-result>`);
    await element.updateComplete;
    
    // Set a subscription
    (element as any).signalRSubscription = { dispose: mockDispose };
    
    // Trigger disconnectedCallback
    element.remove();
    
    // Verify subscription is cleared
    const subscription = (element as any).signalRSubscription;
    expect(subscription).toBeUndefined();
  });

  it('should not throw error when disconnecting without subscription', async () => {
    element = await fixture(html`<page-monitor-result></page-monitor-result>`);
    await element.updateComplete;
    
    // Clear any existing subscription
    (element as any).signalRSubscription = undefined;
    
    // Should not throw when disconnecting
    expect(() => element.remove()).not.toThrow();
  });

  it('should stop hub connection when disconnecting if not already disconnected', async () => {
    const mockStop = vi.fn(() => Promise.resolve());
    
    element = await fixture(html`<page-monitor-result></page-monitor-result>`);
    await element.updateComplete;
    
    // Set a mock hub connection that is connected
    (element as any).hubConnection = {
      state: 'Connected',
      stop: mockStop,
    };
    
    // Trigger disconnectedCallback
    element.remove();
    
    // Verify stop was called
    expect(mockStop).toHaveBeenCalledTimes(1);
  });

  it('should not stop hub connection if already disconnected', async () => {
    const mockStop = vi.fn(() => Promise.resolve());
    
    element = await fixture(html`<page-monitor-result></page-monitor-result>`);
    await element.updateComplete;
    
    // Set a mock hub connection that is already disconnected
    (element as any).hubConnection = {
      state: 'Disconnected',
      stop: mockStop,
    };
    
    // Trigger disconnectedCallback
    element.remove();
    
    // Verify stop was NOT called since already disconnected
    expect(mockStop).not.toHaveBeenCalled();
  });

  it('should handle hub connection stop errors gracefully', async () => {
    const mockStop = vi.fn(() => Promise.reject(new Error('Stop failed')));
    
    element = await fixture(html`<page-monitor-result></page-monitor-result>`);
    await element.updateComplete;
    
    // Set a mock hub connection that fails to stop
    (element as any).hubConnection = {
      state: 'Connected',
      stop: mockStop,
    };
    
    // Trigger disconnectedCallback - should not throw
    expect(() => element.remove()).not.toThrow();
    
    // Wait a bit for async error handling
    await new Promise(resolve => setTimeout(resolve, 10));
    
    // Verify stop was called even though it failed
    expect(mockStop).toHaveBeenCalledTimes(1);
  });
});
