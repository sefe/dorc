import type { ReactiveController, ReactiveControllerHost } from 'lit';
import { NARROW_BREAKPOINT } from './responsive-mixin';

type Host = ReactiveControllerHost & HTMLElement;

/**
 * Owns narrow-mode detection for a migrated grid view (HLPS §3.4).
 *
 * Measures the host's own container width via ResizeObserver — not the
 * viewport — so dialog-hosted grids (make-like-production) collapse when the
 * dialog is narrow, whatever the window size. Replaces ResponsiveMixin in
 * migrated views.
 *
 * The threshold value is imported from the single shared module (SC6).
 * A zero-width measurement (display:none host, dialog not yet opened) is
 * ignored rather than treated as narrow, to avoid mode-flapping while hidden.
 */
export class NarrowListController implements ReactiveController {
  narrow = false;

  private observer: ResizeObserver | undefined;

  constructor(
    private host: Host,
    private breakpoint: number = NARROW_BREAKPOINT
  ) {
    host.addController(this);
  }

  hostConnected(): void {
    this.observer = new ResizeObserver(entries => {
      const width = entries[entries.length - 1].contentRect.width;
      if (width === 0) return;
      const narrow = width <= this.breakpoint;
      if (narrow !== this.narrow) {
        this.narrow = narrow;
        this.host.requestUpdate();
        if (!narrow) this.recalculateGridWidths();
      }
    });
    this.observer.observe(this.host);
  }

  hostDisconnected(): void {
    this.observer?.disconnect();
    this.observer = undefined;
  }

  /**
   * auto-width columns do not re-measure when unhidden (verified against
   * Vaadin 25.2.6 in the HLPS R2 round), so returning to wide triggers an
   * explicit recalculation on every grid in the host's render root.
   */
  private recalculateGridWidths(): void {
    requestAnimationFrame(() => {
      const root = this.host.shadowRoot ?? this.host;
      root
        .querySelectorAll<
          HTMLElement & { recalculateColumnWidths?: () => void }
        >('vaadin-grid')
        .forEach(grid => grid.recalculateColumnWidths?.());
    });
  }
}
