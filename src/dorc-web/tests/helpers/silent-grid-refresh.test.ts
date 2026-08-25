import { describe, it, vi } from 'vitest';
import { expect } from '../_helpers';
import { SilentGridRefresher } from '../../src/helpers/silent-grid-refresh';
import type { Grid } from '@vaadin/grid';

type StubGrid = HTMLElement & {
  clearCache: ReturnType<typeof vi.fn>;
  _flatSize: number | undefined;
  size: number;
};

function makeGrid(flatSize: number | undefined = 200): StubGrid {
  const grid = document.createElement('div') as unknown as StubGrid;
  grid.clearCache = vi.fn();
  grid._flatSize = flatSize;
  grid.size = flatSize ?? 0;
  return grid;
}

function makeRefresher(grid: StubGrid) {
  return new SilentGridRefresher(() => grid as unknown as Grid);
}

describe('SilentGridRefresher', () => {
  it('refresh() flags the grid and invalidates the cache', () => {
    const grid = makeGrid();
    const refresher = makeRefresher(grid);

    refresher.refresh();

    expect(grid.hasAttribute('silent-refresh')).to.be.true;
    expect(grid.clearCache.mock.calls.length).to.equal(1);
  });

  it('reports the preserved count while the refresh is in flight', () => {
    const grid = makeGrid(200);
    const refresher = makeRefresher(grid);

    refresher.refresh();
    refresher.requestStarted();

    expect(refresher.reportedSize(150)).to.equal(200);
  });

  it('reports the real total outside a refresh window', () => {
    const grid = makeGrid(200);
    const refresher = makeRefresher(grid);

    refresher.requestStarted();
    expect(refresher.reportedSize(150)).to.equal(150);
  });

  it('keeps the silent window open until the last in-flight request settles', () => {
    const grid = makeGrid(200);
    const refresher = makeRefresher(grid);

    refresher.refresh();
    refresher.requestStarted();
    refresher.requestStarted();

    refresher.reportedSize(150);
    refresher.requestFinished();
    expect(grid.hasAttribute('silent-refresh')).to.be.true;

    refresher.reportedSize(150);
    refresher.requestFinished();
    expect(grid.hasAttribute('silent-refresh')).to.be.false;
  });

  it('snaps the grid size down when the preserved count masked a shrink', () => {
    const grid = makeGrid(200);
    const refresher = makeRefresher(grid);

    refresher.refresh();
    refresher.requestStarted();
    expect(refresher.reportedSize(150)).to.equal(200);
    refresher.requestFinished();

    expect(grid.size).to.equal(150);
  });

  it('leaves the grid size alone when the result set grew', () => {
    const grid = makeGrid(100);
    grid.size = 120;
    const refresher = makeRefresher(grid);

    refresher.refresh();
    refresher.requestStarted();
    expect(refresher.reportedSize(120)).to.equal(120);
    refresher.requestFinished();

    expect(grid.size).to.equal(120);
  });

  it('drops the visibility override on user interaction but keeps the size guard', () => {
    const grid = makeGrid(200);
    const refresher = makeRefresher(grid);

    refresher.refresh();
    refresher.requestStarted();
    grid.dispatchEvent(new Event('wheel'));

    expect(grid.hasAttribute('silent-refresh')).to.be.false;
    // The size guard still applies until the refresh settles - no mid-scroll
    // size snap.
    expect(refresher.reportedSize(150)).to.equal(200);
  });

  it('resets its state when the grid disappears mid-refresh', () => {
    let grid: StubGrid | undefined = makeGrid(200);
    const refresher = new SilentGridRefresher(
      () => grid as unknown as Grid | undefined
    );

    refresher.refresh();
    refresher.requestStarted();
    grid = undefined;
    refresher.requestFinished();

    // A replacement grid must not inherit the preserved size...
    const newGrid = makeGrid(50);
    grid = newGrid;
    refresher.requestStarted();
    expect(refresher.reportedSize(30)).to.equal(30);
    refresher.requestFinished();

    // ...and must still get interaction listeners on the next refresh.
    refresher.refresh();
    expect(newGrid.hasAttribute('silent-refresh')).to.be.true;
    newGrid.dispatchEvent(new Event('wheel'));
    expect(newGrid.hasAttribute('silent-refresh')).to.be.false;
  });

  it('falls back gracefully when the Vaadin internal is missing', () => {
    const grid = makeGrid();
    grid._flatSize = undefined;
    grid.size = 0;
    const refresher = makeRefresher(grid);

    refresher.refresh();
    refresher.requestStarted();

    expect(refresher.reportedSize(150)).to.equal(150);
  });
});
