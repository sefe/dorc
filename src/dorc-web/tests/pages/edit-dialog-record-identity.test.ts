import { describe, it, vi } from 'vitest';
import { of } from 'rxjs';
import { dialogIn, expect, inDialog } from '../_helpers';

// Both list pages open a shared Edit dialog and swap the record behind it:
// `editingDaemon = {...daemon}` / `editingPermission = permission`, then
// `opened = true`. The form is built by a `dialogRenderer`, and the record
// reaches it through the renderer's dependency array.
//
// If the second Edit shows the first record's values, the user edits — and
// saves — the wrong row.
//
// Measured, not assumed: blanking both arrays leaves the close-then-reopen
// case green, because Vaadin re-invokes the renderer when the overlay opens
// and the renderer reads current state. What the array actually carries is the
// swap of a record *underneath an open dialog* — with `[]` the form keeps
// showing the previous record. Each test covers both, so the first assertion
// pins the user-facing contract and the second is the one that fails if the
// array goes.

const { daemonsSpy, permissionsSpy } = vi.hoisted(() => ({
  daemonsSpy: vi.fn(() =>
    of([
      { Id: 1, Name: 'Alpha', DisplayName: 'Alpha' },
      { Id: 2, Name: 'Beta', DisplayName: 'Beta' }
    ])
  ),
  permissionsSpy: vi.fn(() =>
    of([
      { Id: 1, PermissionName: 'ReadOnly', DisplayName: 'ReadOnly' },
      { Id: 2, PermissionName: 'Owner', DisplayName: 'Owner' }
    ])
  )
}));

vi.mock('../../src/apis/dorc-api', async importOriginal => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    RefDataDaemonsApi: class {
      refDataDaemonsGet = daemonsSpy;
      refDataDaemonsDelete = () => of({});
    },
    ServerDaemonsApi: class {
      serverDaemonsGet = () => of([]);
    },
    RefDataPermissionApi: class {
      refDataPermissionGet = permissionsSpy;
      refDataPermissionDelete = () => of({});
    }
  };
});

await import('../../src/pages/page-daemons-list');
await import('../../src/pages/page-permissions-list');

const settle = () => new Promise(r => setTimeout(r, 250));

// In Vaadin 25 the renderer's output is a light-DOM child of the
// <vaadin-dialog> element itself, which sits in the host's shadow root —
// nothing is appended to document.body. See tests/_helpers.ts.
const formIn = (host: HTMLElement, dialogId: string, tag: string) =>
  inDialog(dialogIn(host, dialogId), tag) as HTMLElement | null;

describe('edit dialogs show the record that was clicked', () => {
  it('daemons: reopening on a second row rebuilds the form for that row', async () => {
    const el = document.createElement('page-daemons-list') as HTMLElement & {
      openEdit(d: unknown): void;
      updateComplete: Promise<unknown>;
    };
    document.body.appendChild(el);
    await el.updateComplete;
    await settle();

    el.openEdit({ Id: 1, Name: 'Alpha', DisplayName: 'Alpha' });
    await el.updateComplete;
    await settle();

    const first = formIn(el, 'edit-daemon-dialog', 'edit-daemon') as
      (HTMLElement & { daemon?: { Name?: string } }) | null;
    expect(first, 'edit form rendered').to.not.equal(null);
    expect(first?.daemon?.Name, 'holds the first row').to.equal('Alpha');

    (
      el as unknown as { editDaemonDialogOpened: boolean }
    ).editDaemonDialogOpened = false;
    await el.updateComplete;
    await settle();

    el.openEdit({ Id: 2, Name: 'Beta', DisplayName: 'Beta' });
    await el.updateComplete;
    await settle();

    const second = formIn(el, 'edit-daemon-dialog', 'edit-daemon') as
      (HTMLElement & { daemon?: { Name?: string } }) | null;
    expect(
      second?.daemon?.Name,
      'holds the second row — editing Beta must not save over Alpha'
    ).to.equal('Beta');

    // And with the dialog still open, swapping the record must reach the form.
    // This is the assertion the dependency array carries.
    el.openEdit({ Id: 3, Name: 'Gamma', DisplayName: 'Gamma' });
    await el.updateComplete;
    await settle();

    expect(
      (
        formIn(el, 'edit-daemon-dialog', 'edit-daemon') as
          (HTMLElement & { daemon?: { Name?: string } }) | null
      )?.daemon?.Name,
      'the open form follows a record swap'
    ).to.equal('Gamma');

    (
      el as unknown as { editDaemonDialogOpened: boolean }
    ).editDaemonDialogOpened = false;
    await settle();
    el.remove();
  });

  it('permissions: reopening on a second row rebuilds the form for that row', async () => {
    const el = document.createElement(
      'page-permissions-list'
    ) as HTMLElement & {
      editPermission(p: unknown): void;
      updateComplete: Promise<unknown>;
    };
    document.body.appendChild(el);
    await el.updateComplete;
    await settle();

    el.editPermission({
      Id: 1,
      PermissionName: 'ReadOnly',
      DisplayName: 'ReadOnly'
    });
    await el.updateComplete;
    await settle();

    // `permission` is private on the element, so read what the user actually
    // sees: the value sitting in the Permission Name field.
    const nameOf = () =>
      (
        formIn(
          el,
          'edit-permission-dialog',
          'edit-permission'
        )?.shadowRoot?.querySelector('#permission-name') as
          (HTMLElement & { value?: string }) | null
      )?.value;

    expect(nameOf(), 'holds the first row').to.equal('ReadOnly');

    (
      el as unknown as { editPermissionDialogOpened: boolean }
    ).editPermissionDialogOpened = false;
    await el.updateComplete;
    await settle();

    el.editPermission({ Id: 2, PermissionName: 'Owner', DisplayName: 'Owner' });
    await el.updateComplete;
    await settle();

    expect(
      nameOf(),
      'holds the second row — editing Owner must not save over ReadOnly'
    ).to.equal('Owner');

    el.editPermission({
      Id: 3,
      PermissionName: 'Auditor',
      DisplayName: 'Auditor'
    });
    await el.updateComplete;
    await settle();

    expect(nameOf(), 'the open form follows a record swap').to.equal('Auditor');

    (
      el as unknown as { editPermissionDialogOpened: boolean }
    ).editPermissionDialogOpened = false;
    await settle();
    el.remove();
  });
});
