import { describe, it, expect, vi, beforeEach } from 'vitest';

// Both permission dialogs seeded their content from a `ref` callback written as
// an inline arrow inside the renderer. Lit's RefDirective compares callback
// identity, so a fresh arrow every render means "old ref → undefined, new ref →
// element" on every invocation — and the renderer runs more than once per open,
// because the overlay calls it itself on `opened-changed` and again when the
// header/footer renderers change.
//
// The view dialog therefore fired four identical GETs per open instead of one,
// with four responses landing into `users` in arbitrary order. The edit dialog
// ran `reset()` four times, the first with the *previous* row's dbId, and any
// later content update on that dialog would re-run `reset()` — which clears the
// selected user, the selected permission and the list.
//
// A stable callback fixes the repetition, but on its own it would break
// row-switching: the ids are what keep the element rendered, so the element
// persists across close and the callback would not re-fire for the next row.
// The ids are cleared on close, so the element is destroyed and recreated, and
// seeding happens exactly once per open.

const { loadSpy, resetSpy, setDbIdSpy } = vi.hoisted(() => ({
  loadSpy: vi.fn(),
  resetSpy: vi.fn(),
  setDbIdSpy: vi.fn<(id: number) => void>()
}));

await import('../../src/components/view-database-permissions');
await import('../../src/components/edit-database-permissions');
const { ViewDatabasePermissions } =
  await import('../../src/components/view-database-permissions');
const { EditDatabasePermissions } =
  await import('../../src/components/edit-database-permissions');
await import('../../src/components/attached-databases');

// Spy on the seeding entry points rather than the network, so the count is of
// "times the dialog was seeded", which is what the defect is about.
type WithDbId = { dbId: number };
ViewDatabasePermissions.prototype.loadDatabaseUsers = function (
  this: WithDbId
) {
  loadSpy(this.dbId);
} as never;
EditDatabasePermissions.prototype.reset = function (this: WithDbId) {
  resetSpy(this.dbId);
} as never;
const realSetDbId = ViewDatabasePermissions.prototype.setDbId;
ViewDatabasePermissions.prototype.setDbId = function (
  this: WithDbId,
  id: number
) {
  setDbIdSpy(id);
  return realSetDbId.call(this as never, id);
} as never;

type Host = HTMLElement & {
  viewDbId: number | null;
  editDbId: number | null;
  viewPermissionsDialogOpened: boolean;
  permissionsDialogOpened: boolean;
};

const settle = async () => {
  for (let i = 0; i < 4; i++) {
    await new Promise(r => requestAnimationFrame(() => r(null)));
  }
};

const openView = async (el: Host, dbId: number) => {
  el.viewDbId = dbId;
  el.viewPermissionsDialogOpened = true;
  await settle();
};

const closeView = async (el: Host) => {
  el.viewPermissionsDialogOpened = false;
  await settle();
};

describe('attached-databases permission dialogs', () => {
  let el: Host;

  beforeEach(async () => {
    loadSpy.mockClear();
    resetSpy.mockClear();
    setDbIdSpy.mockClear();
    el = document.createElement('attached-databases') as Host;
    document.body.appendChild(el);
    await settle();
  });

  it('loads the users exactly once per open', async () => {
    await openView(el, 5);
    expect(loadSpy.mock.calls.length, 'one load per open').toBe(1);
    el.remove();
  });

  it('reloads when reopened for the same database', async () => {
    await openView(el, 5);
    await closeView(el);
    loadSpy.mockClear();

    await openView(el, 5);

    expect(loadSpy.mock.calls.length, 'reopening reloads').toBe(1);
    el.remove();
  });

  it('seeds the new database when switching rows', async () => {
    await openView(el, 5);
    await closeView(el);
    setDbIdSpy.mockClear();

    await openView(el, 7);

    expect(setDbIdSpy.mock.calls.at(-1)?.[0], 'seeded with the new row').toBe(
      7
    );
    el.remove();
  });

  it('resets the edit form exactly once per open', async () => {
    el.editDbId = 5;
    el.permissionsDialogOpened = true;
    await settle();

    expect(resetSpy.mock.calls.length, 'one reset per open').toBe(1);
    el.remove();
  });
});
