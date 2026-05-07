import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// --- Hoisted mock values (available before vi.mock factories run) ---
const { mockRefDataProjectAuditPut } = vi.hoisted(() => {
  const mockSubscribe = vi.fn();
  const mockRefDataProjectAuditPut = vi.fn(() => ({ subscribe: mockSubscribe }));
  return { mockRefDataProjectAuditPut };
});

// --- Mock heavy dependencies (Vaadin web components, helpers, API) ---
vi.mock('@vaadin/button', () => ({}));
vi.mock('@vaadin/grid', () => ({}));
vi.mock('@vaadin/grid/vaadin-grid-column', () => ({}));
vi.mock('@vaadin/grid/vaadin-grid-sort-column', () => ({}));
vi.mock('@vaadin/grid/vaadin-grid-sorter', () => ({}));
vi.mock('@vaadin/horizontal-layout', () => ({}));
vi.mock('@vaadin/icons/vaadin-icons', () => ({}));
vi.mock('@vaadin/icon', () => ({}));
vi.mock('@vaadin/text-field', () => ({}));

vi.mock('../helpers/user-extensions', () => ({
  getShortLogonName: vi.fn((name: string) => name)
}));
vi.mock('../helpers/html-meta-manager', () => ({
  updateMetadata: vi.fn()
}));
vi.mock('../router/routes.ts', () => ({}));
vi.mock('@vaadin/router', () => ({}));

vi.mock('../apis/dorc-api', () => ({
  RefDataProjectAuditApi: class {
    refDataProjectAuditPut = mockRefDataProjectAuditPut;
  },
  PagedDataSorting: class {}
}));

// --- Import component after mocks are defined ---
import { PageProjectsAudit } from './page-projects-audit';

/** Helper: build a fake scroll pane with controlled metrics. */
function makePane(scrollHeight: number, clientHeight: number, scrollTop = 0) {
  const pane = document.createElement('div');
  pane.className = 'diff-pane-scroll';
  Object.defineProperty(pane, 'scrollHeight', { value: scrollHeight, configurable: true });
  Object.defineProperty(pane, 'clientHeight', { value: clientHeight, configurable: true });
  pane.scrollTop = scrollTop;
  pane.scrollTo = vi.fn(({ top }: { top: number }) => {
    pane.scrollTop = top;
  }) as unknown as typeof pane.scrollTo;
  return pane;
}

describe('PageProjectsAudit overview ruler', () => {
  let el: PageProjectsAudit;

  beforeEach(async () => {
    el = new PageProjectsAudit();
    document.body.appendChild(el);
    await el.updateComplete;
  });

  afterEach(() => {
    el.remove();
    document.body.innerHTML = '';
  });

  // -------------------------------------------------------
  // updateOverviewViewport — viewport indicator sizing
  // -------------------------------------------------------
  describe('updateOverviewViewport', () => {
    it('hides the viewport indicator when content fits without scrolling', () => {
      const pane = makePane(100, 100);
      const viewport = document.createElement('div');
      viewport.style.display = '';

      (el as any).updateOverviewViewport(pane, viewport);

      expect(viewport.style.display).toBe('none');
    });

    it('hides the viewport indicator when scrollHeight is less than clientHeight', () => {
      const pane = makePane(50, 100);
      const viewport = document.createElement('div');

      (el as any).updateOverviewViewport(pane, viewport);

      expect(viewport.style.display).toBe('none');
    });

    it('shows the viewport indicator and sizes it proportionally when scrollable', () => {
      // 1000px content, 200px visible window — viewport box should be 20%
      // tall and start at 0% (scrollTop=0).
      const pane = makePane(1000, 200, 0);
      const viewport = document.createElement('div');
      viewport.style.display = 'none';

      (el as any).updateOverviewViewport(pane, viewport);

      expect(viewport.style.display).toBe('');
      expect(viewport.style.top).toBe('0%');
      expect(viewport.style.height).toBe('20%');
    });

    it('positions the viewport indicator according to scrollTop', () => {
      // Scrolled halfway down a 1000px content with a 200px window:
      // top = 500/1000 = 50%, height = 200/1000 = 20%
      const pane = makePane(1000, 200, 500);
      const viewport = document.createElement('div');

      (el as any).updateOverviewViewport(pane, viewport);

      expect(viewport.style.top).toBe('50%');
      expect(viewport.style.height).toBe('20%');
    });
  });

  // -------------------------------------------------------
  // onOverviewClick — click-to-jump
  // -------------------------------------------------------
  describe('onOverviewClick', () => {
    /** Wire a fake overview ruler with a sibling pane the handler can find. */
    function mountOverview(pane: HTMLDivElement) {
      const wrapper = document.createElement('div');
      const overview = document.createElement('div');
      overview.className = 'diff-overview';
      overview.getBoundingClientRect = () =>
        ({ top: 0, height: 100, left: 0, right: 0, bottom: 100, width: 12, x: 0, y: 0, toJSON: () => ({}) } as DOMRect);
      wrapper.appendChild(pane);
      wrapper.appendChild(overview);
      document.body.appendChild(wrapper);
      return overview;
    }

    it('scrolls to (ratio * scrollHeight) - clientHeight/2 on click', () => {
      // Click at 50% of a 100px-tall ruler, content is 1000px tall, window 200px:
      // target = 0.5 * 1000 - 100 = 400
      const pane = makePane(1000, 200);
      const overview = mountOverview(pane);
      const ev = { currentTarget: overview, clientY: 50 } as unknown as MouseEvent;

      (el as any).onOverviewClick(ev);

      expect(pane.scrollTo).toHaveBeenCalledWith(
        expect.objectContaining({ top: 400, behavior: 'smooth' })
      );
    });

    it('clamps the scroll target to 0 when the click would land above the top', () => {
      // Click near the top: target = 0.05 * 1000 - 100 = -50 → clamp to 0.
      const pane = makePane(1000, 200);
      const overview = mountOverview(pane);
      const ev = { currentTarget: overview, clientY: 5 } as unknown as MouseEvent;

      (el as any).onOverviewClick(ev);

      expect(pane.scrollTo).toHaveBeenCalledWith(
        expect.objectContaining({ top: 0, behavior: 'smooth' })
      );
    });

    it('clamps the scroll target to (scrollHeight - clientHeight) at the bottom', () => {
      // Click at the very bottom: raw target = 1000 - 100 = 900, but max
      // useful scroll position is scrollHeight - clientHeight = 800.
      const pane = makePane(1000, 200);
      const overview = mountOverview(pane);
      const ev = { currentTarget: overview, clientY: 100 } as unknown as MouseEvent;

      (el as any).onOverviewClick(ev);

      expect(pane.scrollTo).toHaveBeenCalledWith(
        expect.objectContaining({ top: 800, behavior: 'smooth' })
      );
    });

    it('does nothing when no scroll pane sibling is present', () => {
      const overview = document.createElement('div');
      const wrapper = document.createElement('div');
      wrapper.appendChild(overview);
      document.body.appendChild(wrapper);
      const ev = { currentTarget: overview, clientY: 50 } as unknown as MouseEvent;

      // Should not throw and there's nothing to assert other than no crash.
      expect(() => (el as any).onOverviewClick(ev)).not.toThrow();
    });

    it('does nothing when the ruler has zero height (avoids NaN scrollTo)', () => {
      // A collapsed ruler would yield ratio = clientY / 0 = ±Infinity / NaN
      // and taint the scrollTo target. The handler must early-return instead.
      const pane = makePane(1000, 200);
      const wrapper = document.createElement('div');
      const overview = document.createElement('div');
      overview.className = 'diff-overview';
      overview.getBoundingClientRect = () =>
        ({ top: 0, height: 0, left: 0, right: 0, bottom: 0, width: 12, x: 0, y: 0, toJSON: () => ({}) } as DOMRect);
      wrapper.appendChild(pane);
      wrapper.appendChild(overview);
      document.body.appendChild(wrapper);
      const ev = { currentTarget: overview, clientY: 50 } as unknown as MouseEvent;

      (el as any).onOverviewClick(ev);

      expect(pane.scrollTo).not.toHaveBeenCalled();
    });

    it('clamps clientY above the ruler to the top of the scrollable range', () => {
      // clientY = -50 is above the ruler — ratio would be negative; clamp to 0
      // so the target is computed at the very top (pre-clamp 0 - 100 = -100,
      // post-clamp 0).
      const pane = makePane(1000, 200);
      const overview = mountOverview(pane);
      const ev = { currentTarget: overview, clientY: -50 } as unknown as MouseEvent;

      (el as any).onOverviewClick(ev);

      expect(pane.scrollTo).toHaveBeenCalledWith(
        expect.objectContaining({ top: 0, behavior: 'smooth' })
      );
    });

    it('clamps clientY below the ruler to the bottom of the scrollable range', () => {
      // clientY = 9999 is far below the ruler — ratio would be > 1; clamp to 1
      // so target = 1000 - 100 = 900, then bottom-clamped to 800.
      const pane = makePane(1000, 200);
      const overview = mountOverview(pane);
      const ev = { currentTarget: overview, clientY: 9999 } as unknown as MouseEvent;

      (el as any).onOverviewClick(ev);

      expect(pane.scrollTo).toHaveBeenCalledWith(
        expect.objectContaining({ top: 800, behavior: 'smooth' })
      );
    });
  });

  // -------------------------------------------------------
  // onOverviewKeydown — keyboard cycling through markers
  // -------------------------------------------------------
  describe('onOverviewKeydown', () => {
    /** Build an overview with markers at the given top% values. */
    function mountWithMarkers(pane: HTMLDivElement, markerTopPercents: number[]) {
      const wrapper = document.createElement('div');
      const overview = document.createElement('div');
      overview.className = 'diff-overview';
      for (const pct of markerTopPercents) {
        const m = document.createElement('div');
        m.className = 'diff-overview-marker';
        m.style.top = `${pct}%`;
        overview.appendChild(m);
      }
      wrapper.appendChild(pane);
      wrapper.appendChild(overview);
      document.body.appendChild(wrapper);
      return overview;
    }

    it('Enter jumps to the next change marker after the viewport centre', () => {
      // Markers at 100, 500, 900 px (10/50/90% of a 1000px content).
      // Viewport at scrollTop=0, clientHeight=200 → centre=100.
      // Next marker > 101 is 500 → scroll target = 500 - 100 = 400.
      const pane = makePane(1000, 200, 0);
      const overview = mountWithMarkers(pane, [10, 50, 90]);
      const ev = new KeyboardEvent('keydown', { key: 'Enter' });
      Object.defineProperty(ev, 'currentTarget', { value: overview });

      (el as any).onOverviewKeydown(ev);

      expect(pane.scrollTo).toHaveBeenCalledWith(
        expect.objectContaining({ top: 400 })
      );
    });

    it('ArrowUp jumps to the previous change marker', () => {
      // scrollTop=600 → centre=700. Previous marker < 699 is 500 → target=400.
      const pane = makePane(1000, 200, 600);
      const overview = mountWithMarkers(pane, [10, 50, 90]);
      const ev = new KeyboardEvent('keydown', { key: 'ArrowUp' });
      Object.defineProperty(ev, 'currentTarget', { value: overview });

      (el as any).onOverviewKeydown(ev);

      expect(pane.scrollTo).toHaveBeenCalledWith(
        expect.objectContaining({ top: 400 })
      );
    });

    it('Enter cycles to the first marker when past the last', () => {
      // scrollTop=900 → centre=1000. No marker > 1001, cycle to 100 → target=0.
      const pane = makePane(1000, 200, 900);
      const overview = mountWithMarkers(pane, [10, 50, 90]);
      const ev = new KeyboardEvent('keydown', { key: 'Enter' });
      Object.defineProperty(ev, 'currentTarget', { value: overview });

      (el as any).onOverviewKeydown(ev);

      expect(pane.scrollTo).toHaveBeenCalledWith(
        expect.objectContaining({ top: 0 })
      );
    });

    it('ignores keys other than navigation keys', () => {
      const pane = makePane(1000, 200, 0);
      const overview = mountWithMarkers(pane, [10, 50, 90]);
      const ev = new KeyboardEvent('keydown', { key: 'a' });
      Object.defineProperty(ev, 'currentTarget', { value: overview });

      (el as any).onOverviewKeydown(ev);

      expect(pane.scrollTo).not.toHaveBeenCalled();
    });

    it('does nothing when there are no markers', () => {
      const pane = makePane(1000, 200, 0);
      const overview = mountWithMarkers(pane, []);
      const ev = new KeyboardEvent('keydown', { key: 'Enter' });
      Object.defineProperty(ev, 'currentTarget', { value: overview });

      (el as any).onOverviewKeydown(ev);

      expect(pane.scrollTo).not.toHaveBeenCalled();
    });
  });
});
