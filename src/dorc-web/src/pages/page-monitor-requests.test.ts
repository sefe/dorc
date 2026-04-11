import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// --- Hoisted mock values (available before vi.mock factories run) ---
const { mockRequestStatusesPut, mockSubscribe } = vi.hoisted(() => {
  const mockSubscribe = vi.fn();
  const mockRequestStatusesPut = vi.fn(() => ({ subscribe: mockSubscribe }));
  return { mockRequestStatusesPut, mockSubscribe };
});

// --- Mock all heavy dependencies ---

// Vaadin web component side-effect registrations
vi.mock('@vaadin/grid/vaadin-grid', () => ({}));
vi.mock('@vaadin/grid/vaadin-grid-column', () => ({}));
vi.mock('@vaadin/grid/vaadin-grid-filter', () => ({}));
vi.mock('@vaadin/grid/vaadin-grid-sort-column', () => ({}));
vi.mock('@vaadin/grid/vaadin-grid-sorter', () => ({}));
vi.mock('@vaadin/icons/vaadin-icons', () => ({}));
vi.mock('@vaadin/text-field', () => ({}));
vi.mock('@vaadin/notification', () => ({
  Notification: { show: vi.fn() }
}));
vi.mock('@vaadin/grid', () => ({}));

// Internal component side-effect registrations
vi.mock('../components/grid-button-groups/request-controls', () => ({}));
vi.mock('../icons/iron-icons.js', () => ({}));
vi.mock('../icons/custom-icons.js', () => ({}));
vi.mock('../components/notifications/error-notification', () => ({
  ErrorNotification: class {}
}));
vi.mock('../components/connection-status-indicator', () => ({}));

// SignalR
vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: {
    Disconnected: 'Disconnected',
    Connected: 'Connected'
  }
}));
vi.mock('../services/ServerEvents', () => ({
  DeploymentHub: {
    getConnection: vi.fn(() => ({
      onclose: vi.fn(),
      onreconnecting: vi.fn(),
      onreconnected: vi.fn(),
      start: vi.fn(() => Promise.resolve()),
      stop: vi.fn(() => Promise.resolve()),
      state: 'Disconnected'
    }))
  },
  getReceiverRegister: vi.fn(() => ({ register: vi.fn() }))
}));

// Helpers & router
vi.mock('../helpers/user-extensions.js', () => ({
  getShortLogonName: vi.fn((name: string) => name)
}));
vi.mock('../helpers/errorMessage-retriever.js', () => ({
  retrieveErrorMessage: vi.fn((err: unknown) => String(err))
}));
vi.mock('../helpers/html-meta-manager', () => ({
  updateMetadata: vi.fn()
}));
vi.mock('../router/routes.ts', () => ({}));
vi.mock('@vaadin/router', () => ({}));

// DOrc API
vi.mock('../apis/dorc-api', () => ({
  RequestStatusesApi: class {
    requestStatusesPut = mockRequestStatusesPut;
  }
}));

// --- Import component after mocks are defined ---
import { PageMonitorRequests } from './page-monitor-requests';

/** Flush microtask queue so async firstUpdated (SignalR init) completes. */
async function flushAsync(): Promise<void> {
  await new Promise(resolve => setTimeout(resolve, 0));
}

describe('PageMonitorRequests', () => {
  let el: PageMonitorRequests;

  beforeEach(async () => {
    // Use class constructor directly — more reliable than document.createElement in jsdom
    el = new PageMonitorRequests();
    document.body.appendChild(el);
    await el.updateComplete;
    await flushAsync();

    // Provide clearCache on the mock grid element (real Vaadin grid is not loaded)
    const grid = el.shadowRoot?.querySelector('#grid');
    if (grid) {
      (grid as any).clearCache = vi.fn();
    }

    mockRequestStatusesPut.mockClear();
    mockSubscribe.mockClear();
    mockSubscribe.mockImplementation((handlers: any) => {
      handlers.next?.({ Items: [], TotalItems: 0 });
      handlers.complete?.();
    });
  });

  afterEach(() => {
    el.remove();
    vi.useRealTimers();
    document.body.innerHTML = '';
  });

  // -------------------------------------------------------
  // Initial state
  // -------------------------------------------------------
  describe('initial state', () => {
    it('has empty project filter', () => {
      expect(el.projectFilter).toBe('');
    });

    it('has empty env filter', () => {
      expect(el.envFilter).toBe('');
    });

    it('has empty build filter', () => {
      expect(el.buildFilter).toBe('');
    });

    it('does not have the old combined detailsFilter property', () => {
      expect((el as any).detailsFilter).toBeUndefined();
    });
  });

  // -------------------------------------------------------
  // Filter event routing (via searching-requests-started)
  // -------------------------------------------------------
  describe('filter event routing', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    it('routes Project field to projectFilter', () => {
      el.dispatchEvent(
        new CustomEvent('searching-requests-started', {
          detail: { field: 'Project', value: 'MyProject' }
        })
      );
      vi.advanceTimersByTime(500);
      expect(el.projectFilter).toBe('MyProject');
    });

    it('routes EnvironmentName field to envFilter', () => {
      el.dispatchEvent(
        new CustomEvent('searching-requests-started', {
          detail: { field: 'EnvironmentName', value: 'staging' }
        })
      );
      vi.advanceTimersByTime(500);
      expect(el.envFilter).toBe('staging');
    });

    it('routes BuildNumber field to buildFilter', () => {
      el.dispatchEvent(
        new CustomEvent('searching-requests-started', {
          detail: { field: 'BuildNumber', value: '1.2.3' }
        })
      );
      vi.advanceTimersByTime(500);
      expect(el.buildFilter).toBe('1.2.3');
    });

    it('still routes Username field correctly', () => {
      el.dispatchEvent(
        new CustomEvent('searching-requests-started', {
          detail: { field: 'Username', value: 'testuser' }
        })
      );
      vi.advanceTimersByTime(500);
      expect(el.userFilter).toBe('testuser');
    });

    it('still routes Status field correctly', () => {
      el.dispatchEvent(
        new CustomEvent('searching-requests-started', {
          detail: { field: 'Status', value: 'Running' }
        })
      );
      vi.advanceTimersByTime(500);
      expect(el.statusFilter).toBe('Running');
    });
  });

  // -------------------------------------------------------
  // DataProvider — filter construction
  // -------------------------------------------------------
  describe('dataProvider filter construction', () => {
    function getDataProvider(): (...args: any[]) => void {
      const grid = el.shadowRoot?.querySelector('#grid') as any;
      return grid?.dataProvider;
    }

    function callDataProvider(
      overrides: { filters?: any[]; sortOrders?: any[] } = {}
    ) {
      const dp = getDataProvider();
      const params = {
        page: 0,
        pageSize: 50,
        filters: overrides.filters ?? [],
        sortOrders: overrides.sortOrders ?? [{ path: 'Id', direction: 'desc' }]
      };
      const callback = vi.fn();
      dp(params, callback);
      return { params, callback };
    }

    it('sends Project filter when projectFilter is set', () => {
      el.projectFilter = 'Alpha';
      callDataProvider();

      const args = mockRequestStatusesPut.mock.calls[0][0];
      expect(args.pagedDataOperators.Filters).toContainEqual(
        expect.objectContaining({ Path: 'Project', FilterValue: 'Alpha' })
      );
    });

    it('sends EnvironmentName filter when envFilter is set', () => {
      el.envFilter = 'staging';
      callDataProvider();

      const args = mockRequestStatusesPut.mock.calls[0][0];
      expect(args.pagedDataOperators.Filters).toContainEqual(
        expect.objectContaining({
          Path: 'EnvironmentName',
          FilterValue: 'staging'
        })
      );
    });

    it('sends BuildNumber filter when buildFilter is set', () => {
      el.buildFilter = '2.0.1';
      callDataProvider();

      const args = mockRequestStatusesPut.mock.calls[0][0];
      expect(args.pagedDataOperators.Filters).toContainEqual(
        expect.objectContaining({
          Path: 'BuildNumber',
          FilterValue: '2.0.1'
        })
      );
    });

    it('sends all three filters independently when all are set', () => {
      el.projectFilter = 'Alpha';
      el.envFilter = 'production';
      el.buildFilter = '5.0';
      callDataProvider();

      const filters =
        mockRequestStatusesPut.mock.calls[0][0].pagedDataOperators.Filters;

      expect(filters).toContainEqual(
        expect.objectContaining({ Path: 'Project', FilterValue: 'Alpha' })
      );
      expect(filters).toContainEqual(
        expect.objectContaining({
          Path: 'EnvironmentName',
          FilterValue: 'production'
        })
      );
      expect(filters).toContainEqual(
        expect.objectContaining({
          Path: 'BuildNumber',
          FilterValue: '5.0'
        })
      );
    });

    it('does not send filters for empty values', () => {
      el.projectFilter = '';
      el.envFilter = 'staging';
      el.buildFilter = '';
      callDataProvider();

      const filters =
        mockRequestStatusesPut.mock.calls[0][0].pagedDataOperators.Filters;

      expect(filters).toContainEqual(
        expect.objectContaining({
          Path: 'EnvironmentName',
          FilterValue: 'staging'
        })
      );
      expect(filters).not.toContainEqual(
        expect.objectContaining({ Path: 'Project' })
      );
      expect(filters).not.toContainEqual(
        expect.objectContaining({ Path: 'BuildNumber' })
      );
    });
  });

  // -------------------------------------------------------
  // DataProvider — multi-sort support
  // -------------------------------------------------------
  describe('dataProvider multi-sort', () => {
    function getDataProvider(): (...args: any[]) => void {
      const grid = el.shadowRoot?.querySelector('#grid') as any;
      return grid?.dataProvider;
    }

    it('allows multiple sort orders (no longer bails)', () => {
      const dp = getDataProvider();
      dp(
        {
          page: 0,
          pageSize: 50,
          filters: [],
          sortOrders: [
            { path: 'Project', direction: 'asc' },
            { path: 'EnvironmentName', direction: 'desc' }
          ]
        },
        vi.fn()
      );

      expect(mockRequestStatusesPut).toHaveBeenCalled();
      const sortOrders =
        mockRequestStatusesPut.mock.calls[0][0].pagedDataOperators.SortOrders;
      expect(sortOrders).toHaveLength(2);
    });

    it('allows zero sort orders', () => {
      const dp = getDataProvider();
      dp({ page: 0, pageSize: 50, filters: [], sortOrders: [] }, vi.fn());

      expect(mockRequestStatusesPut).toHaveBeenCalled();
    });

    it('passes sort path and direction to API', () => {
      const dp = getDataProvider();
      dp(
        {
          page: 0,
          pageSize: 50,
          filters: [],
          sortOrders: [{ path: 'BuildNumber', direction: 'asc' }]
        },
        vi.fn()
      );

      const sortOrders =
        mockRequestStatusesPut.mock.calls[0][0].pagedDataOperators.SortOrders;
      expect(sortOrders).toContainEqual(
        expect.objectContaining({ Path: 'BuildNumber', Direction: 'asc' })
      );
    });
  });

  // -------------------------------------------------------
  // Details header renderer
  // -------------------------------------------------------
  describe('detailsHeaderRenderer', () => {
    let root: HTMLElement;

    beforeEach(() => {
      root = document.createElement('div');
      el.detailsHeaderRenderer(root);
    });

    it('renders three filter text inputs', () => {
      const inputs = root.querySelectorAll('vaadin-text-field');
      expect(inputs.length).toBe(3);
    });

    it('uses correct placeholder labels', () => {
      const inputs = root.querySelectorAll('vaadin-text-field');
      expect(inputs[0].getAttribute('placeholder')).toBe('Project');
      expect(inputs[1].getAttribute('placeholder')).toBe('Environment');
      expect(inputs[2].getAttribute('placeholder')).toBe('Build');
    });

    it('renders three sort toggles', () => {
      const sorters = root.querySelectorAll('vaadin-grid-sorter');
      expect(sorters.length).toBe(3);
    });

    it('maps sort paths to correct model fields', () => {
      const sorters = root.querySelectorAll('vaadin-grid-sorter');
      expect(sorters[0].getAttribute('path')).toBe('Project');
      expect(sorters[1].getAttribute('path')).toBe('EnvironmentName');
      expect(sorters[2].getAttribute('path')).toBe('BuildNumber');
    });

    it('lays out all filters in a single row with a dash separator', () => {
      const container = root.querySelector('div');
      expect(container).not.toBeNull();
      expect(container!.style.display).toBe('flex');

      const inputs = container!.querySelectorAll('vaadin-text-field');
      expect(inputs.length).toBe(3);
      expect(inputs[0].getAttribute('placeholder')).toBe('Project');
      expect(inputs[1].getAttribute('placeholder')).toBe('Environment');
      expect(inputs[2].getAttribute('placeholder')).toBe('Build');

      expect(container!.querySelector('span')?.textContent).toBe('-');
    });
  });

  // -------------------------------------------------------
  // Details cell renderer (combined display preserved)
  // -------------------------------------------------------
  describe('details cell renderer', () => {
    it('renders project, environment, and build in a single cell', () => {
      const root = document.createElement('div');
      const model = {
        item: {
          Project: 'TestProject',
          EnvironmentName: 'production',
          BuildNumber: '3.1.4'
        }
      };

      (el as any).detailsRenderer(root, document.createElement('div'), model);

      const text = root.textContent ?? '';
      expect(text).toContain('TestProject');
      expect(text).toContain('production');
      expect(text).toContain('3.1.4');
    });
  });
});
