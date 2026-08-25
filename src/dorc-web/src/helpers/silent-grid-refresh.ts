import { css } from 'lit';
import type { Grid } from '@vaadin/grid';

/**
 * Styles that keep the previous cell content visible while a silent refresh
 * is in flight. Include in the static styles of any component that uses
 * SilentGridRefresher.
 *
 * Vaadin hides the slotted content of loading rows (visibility: hidden),
 * which makes the whole grid flash blank on every background refresh; an
 * outer-tree rule overrides the shadow ::slotted rule while the
 * silent-refresh attribute is present.
 */
export const silentRefreshStyles = css`
  vaadin-grid[silent-refresh] vaadin-grid-cell-content {
    visibility: visible;
  }
`;

/**
 * Coordinates flash-free background refreshes of a vaadin-grid backed by a
 * paged data provider.
 *
 * During a silent refresh the previously cached row count is preserved (so
 * the grid size doesn't collapse and jump the scroll position) and the
 * silent-refresh attribute keeps existing cell content visible. The window
 * is torn down only once every in-flight page request has settled - tearing
 * down on the first response would blank the pages still loading. If the
 * preserved count masked a shrink in the result set, the grid size is
 * snapped down to the real total at teardown so the grid never stays
 * oversized.
 *
 * The visibility override is also dropped as soon as the user interacts
 * with the grid (wheel/touch/mouse/keys): recycled rows would otherwise
 * briefly show another item's content while scrolling, and Vaadin's default
 * hidden-loading behaviour is the right look mid-interaction.
 */
export class SilentGridRefresher {
  private pendingRequests = 0;

  private preservedCount = 0;

  private lastTotal = 0;

  private sawResponse = false;

  private listenersAttached = false;

  constructor(private readonly getGrid: () => Grid | undefined) {}

  private readonly onUserInteraction = () => {
    const grid = this.getGrid();
    if (!grid) return;
    grid.removeAttribute('silent-refresh');
    this.detachInteractionListeners(grid);
  };

  /**
   * Guarded read of Vaadin grid's private _flatSize (the row count the grid
   * currently knows about). The previous internal (__data._flatSize) broke
   * silently on the Vaadin 25 upgrade, so fall back to the preserved count
   * if this one is ever renamed too.
   */
  private get cachedRowCount(): number {
    return (this.getGrid() as any)?._flatSize ?? this.preservedCount;
  }

  /** Background (e.g. SignalR-triggered) refresh: no flash, no scroll jump. */
  refresh(): void {
    const grid = this.getGrid();
    if (!grid) return;
    this.preservedCount = this.cachedRowCount;
    this.sawResponse = false;
    grid.setAttribute('silent-refresh', '');
    this.attachInteractionListeners(grid);
    grid.clearCache();
  }

  /** Manual refresh: preserve the size but let the loading UI show. */
  refreshWithLoadingUi(): void {
    const grid = this.getGrid();
    if (!grid) return;
    this.preservedCount = this.cachedRowCount;
    this.sawResponse = false;
    grid.clearCache();
  }

  /** Call when the data provider issues a page request. */
  requestStarted(): void {
    this.pendingRequests += 1;
  }

  /** Size to report to the grid's data provider callback. */
  reportedSize(totalItems: number | null | undefined): number {
    this.lastTotal = totalItems ?? 0;
    this.sawResponse = true;
    return Math.max(this.preservedCount, this.lastTotal);
  }

  /**
   * Call when a page request settles (success or error). Tears the silent
   * window down once no requests remain in flight.
   */
  requestFinished(): void {
    this.pendingRequests = Math.max(0, this.pendingRequests - 1);
    if (this.pendingRequests > 0) return;

    const grid = this.getGrid();
    if (grid) {
      grid.removeAttribute('silent-refresh');
      this.detachInteractionListeners(grid);
      if (this.sawResponse && this.preservedCount > this.lastTotal) {
        // The preserved count masked a shrink; snap to the real total so the
        // grid doesn't keep blank scroll space at the end.
        grid.size = this.lastTotal;
      }
    } else {
      // Grid detached/replaced mid-refresh: the listeners died with the old
      // element, so clear the flag or a future grid never gets them.
      this.listenersAttached = false;
    }
    this.preservedCount = 0;
  }

  private attachInteractionListeners(grid: Grid): void {
    if (this.listenersAttached) return;
    this.listenersAttached = true;
    grid.addEventListener('wheel', this.onUserInteraction, { passive: true });
    grid.addEventListener('touchstart', this.onUserInteraction, { passive: true });
    grid.addEventListener('mousedown', this.onUserInteraction);
    grid.addEventListener('keydown', this.onUserInteraction);
  }

  private detachInteractionListeners(grid: Grid): void {
    if (!this.listenersAttached) return;
    this.listenersAttached = false;
    grid.removeEventListener('wheel', this.onUserInteraction);
    grid.removeEventListener('touchstart', this.onUserInteraction);
    grid.removeEventListener('mousedown', this.onUserInteraction);
    grid.removeEventListener('keydown', this.onUserInteraction);
  }
}
