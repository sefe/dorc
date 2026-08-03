import { dialogIn, expect, fixture, html, settle } from '../_helpers';
import '../../src/components/add-edit-access-control';

// The three permission columns used to render a checkbox, query it back out of
// the cell, and attach a listener that flipped a bit in the row model. They are
// templates with inline handlers now, and they read `this.UserEditable` and
// friends directly rather than through a component reference stashed on the
// grid column. These tests cover both halves of that: the bits still toggle,
// and the disabled state tracks the component's own flags.

// Mirrors the bit values in add-edit-access-control.ts.
const AC_ALLOW_WRITE = 1;
const AC_ALLOW_READ_SECRETS = 2;
const AC_ALLOW_OWNER = 4;

type AccessControl = { Name?: string; Allow?: number };

type Host = HTMLElement & {
  Privileges?: AccessControl[];
  UserEditable: boolean;
  UserIsOwner: boolean;
  UserCanReadSecrets: boolean;
  updateComplete: Promise<unknown>;
};

const mount = async (privileges: AccessControl[]) => {
  const el = (await fixture(
    html`<add-edit-access-control></add-edit-access-control>`
  )) as unknown as Host;
  el.Privileges = privileges;
  el.UserEditable = true;
  el.UserIsOwner = true;
  el.UserCanReadSecrets = true;
  // The grid lives inside the dialog's renderer, so it does not exist until
  // the dialog opens. `open()` would go to the API; this is the same state.
  (el as unknown as { dialogOpened: boolean }).dialogOpened = true;
  await settle();
  await settle();
  return el;
};

/**
 * The checkbox in one body cell, addressed by row and column index.
 *
 * Vaadin renders cell content into `vaadin-grid-cell-content` elements that sit
 * in the grid's light DOM in creation order, not visual order — so the cells
 * are reached through the slot each `<td>` points at instead.
 */
const COLUMN = { write: 1, readSecrets: 2, owner: 3 };

/**
 * Clicks a checkbox the way a user would.
 *
 * The handlers listen for `change`, which is gesture-only — assigning
 * `.checked` from a test fires nothing, by design: that is what stops a
 * recycled grid cell writing to the row it used to hold.
 */
const clickCheckbox = (
  checkbox: HTMLElement & { checked: boolean; disabled: boolean }
) => {
  (checkbox.querySelector('input') as HTMLInputElement).click();
};

const checkboxAt = (el: Host, row: number, column: number) => {
  const grid = dialogIn(el)?.querySelector('vaadin-grid');
  const rows = grid?.shadowRoot?.querySelectorAll('#items > tr') ?? [];
  const cell = rows[row]?.querySelectorAll('td')[column];
  const slot = cell?.querySelector('slot');
  const content = slot?.assignedElements()[0];
  return content?.querySelector('vaadin-checkbox') as HTMLElement & {
    checked: boolean;
    disabled: boolean;
  };
};

describe('add-edit-access-control permission checkboxes', () => {
  it('renders a checkbox in each permission column', async () => {
    const el = await mount([{ Name: 'someone', Allow: 0 }]);
    expect(checkboxAt(el, 0, COLUMN.write), 'write').to.not.equal(null);
    expect(checkboxAt(el, 0, COLUMN.readSecrets), 'read secrets').to.not.equal(
      null
    );
    expect(checkboxAt(el, 0, COLUMN.owner), 'owner').to.not.equal(null);
  });

  it('reflects the bits already set on the row', async () => {
    const el = await mount([
      { Name: 'someone', Allow: AC_ALLOW_WRITE | AC_ALLOW_OWNER }
    ]);
    expect(checkboxAt(el, 0, COLUMN.write).checked, 'write').to.equal(true);
    expect(
      checkboxAt(el, 0, COLUMN.readSecrets).checked,
      'read secrets'
    ).to.equal(false);
    expect(checkboxAt(el, 0, COLUMN.owner).checked, 'owner').to.equal(true);
  });

  it('sets the bit when a permission is ticked', async () => {
    const privilege: AccessControl = { Name: 'someone', Allow: 0 };
    const el = await mount([privilege]);

    clickCheckbox(checkboxAt(el, 0, COLUMN.write));
    await settle();

    expect(privilege.Allow! & AC_ALLOW_WRITE).to.equal(AC_ALLOW_WRITE);
  });

  it('clears the bit when a permission is unticked', async () => {
    const privilege: AccessControl = {
      Name: 'someone',
      Allow: AC_ALLOW_WRITE | AC_ALLOW_READ_SECRETS
    };
    const el = await mount([privilege]);

    clickCheckbox(checkboxAt(el, 0, COLUMN.readSecrets));
    await settle();

    expect(privilege.Allow! & AC_ALLOW_READ_SECRETS).to.equal(0);
    expect(privilege.Allow! & AC_ALLOW_WRITE, 'write untouched').to.equal(
      AC_ALLOW_WRITE
    );
  });

  it('refuses a third owner and reverts the checkbox', async () => {
    const third: AccessControl = { Name: 'third', Allow: 0 };
    const el = await mount([
      { Name: 'first', Allow: AC_ALLOW_OWNER },
      { Name: 'second', Allow: AC_ALLOW_OWNER },
      third
    ]);

    const owner = checkboxAt(el, 2, COLUMN.owner);
    clickCheckbox(owner);
    await settle();

    expect(third.Allow! & AC_ALLOW_OWNER, 'bit not set').to.equal(0);
    expect(owner.checked, 'checkbox reverted').to.equal(false);
  });

  it('disables the write column when the user cannot edit', async () => {
    const el = await mount([{ Name: 'someone', Allow: 0 }]);
    expect(checkboxAt(el, 0, COLUMN.write).disabled).to.equal(false);

    // No requestContentUpdate() anywhere in the component — the dependency
    // array on columnBodyRenderer is what repaints the cell.
    el.UserEditable = false;
    await settle();

    expect(checkboxAt(el, 0, COLUMN.write).disabled).to.equal(true);
  });

  it('disables the owner column when the user is not an owner', async () => {
    const el = await mount([{ Name: 'someone', Allow: 0 }]);
    expect(checkboxAt(el, 0, COLUMN.owner).disabled).to.equal(false);

    el.UserIsOwner = false;
    await settle();

    expect(checkboxAt(el, 0, COLUMN.owner).disabled).to.equal(true);
  });

  // Clicking the input fires `change` AND `checked-changed`, so a click test
  // cannot tell the two bindings apart. Setting the property fires only
  // `checked-changed` — which is exactly what a recycled cell does when Lit
  // commits the next row's value. If these renderers ever go back to listening
  // for it, this is the row that gets corrupted.
  it('ignores a programmatic checked change, as a recycled cell produces', async () => {
    const privilege: AccessControl = { Name: 'someone', Allow: 0 };
    const el = await mount([privilege]);

    checkboxAt(el, 0, COLUMN.write).checked = true;
    await settle();

    expect(privilege.Allow, 'no write from a non-gesture change').to.equal(0);
  });

  it('ignores a programmatic owner change', async () => {
    const privilege: AccessControl = { Name: 'someone', Allow: 0 };
    const el = await mount([privilege]);

    checkboxAt(el, 0, COLUMN.owner).checked = true;
    await settle();

    expect(privilege.Allow, 'owner bit not set').to.equal(0);
  });
});
