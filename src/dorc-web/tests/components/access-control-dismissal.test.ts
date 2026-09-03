import { dialogIn, expect, fixture, html, settle } from '../_helpers';
import '../../src/components/add-edit-access-control';

// This dialog was a `<paper-dialog modal>` before the migration, and `modal`
// implies `noCancelOnOutsideClick` and `noCancelOnEscKey`
// (paper-dialog-behavior.js) — so neither Escape nor a stray click could close
// it. Converting it to a bare `<vaadin-dialog>` handed it both, and the new
// `opened-changed` handler calls `resetDialogState()`, which sets
// `Privileges = []`.
//
// `Privileges` is where every unsaved edit lives: users added from the AD
// search, and the permission bits ticked on them. Nothing is persisted until
// Save. So a mis-aimed click could silently discard an entire session's work,
// and reopening would not bring it back — `open()` refetches from the API.
//
// The other converted dialogs dismiss freely on purpose. This one opts out.

type Host = HTMLElement & {
  Privileges?: { Name?: string; Allow?: number }[];
  UserEditable: boolean;
  dialogOpened?: boolean;
};

const mount = async () => {
  const el = (await fixture(
    html`<add-edit-access-control></add-edit-access-control>`
  )) as unknown as Host;
  el.UserEditable = true;
  el.Privileges = [
    { Name: 'alice', Allow: 1 },
    { Name: 'bob', Allow: 0 }
  ];
  (el as unknown as { dialogOpened: boolean }).dialogOpened = true;
  await settle();
  await settle();
  return el;
};

const overlayOf = (el: Host) =>
  el.shadowRoot?.querySelector('vaadin-dialog')?.shadowRoot?.querySelector(
    'vaadin-dialog-overlay'
  ) as HTMLElement | null;

describe('access control dialog dismissal', () => {
  it('keeps action spacing and save position stable while saving', async () => {
    const el = await mount();
    const dialog = dialogIn(el)!;
    const actionBar = dialog.querySelector('.dialog-actions') as HTMLElement;
    const saveButton = dialog.querySelector(
      '#save-access-controls'
    ) as HTMLElement;
    const idlePosition = saveButton.getBoundingClientRect();

    (el as unknown as { savingAccessControls: boolean }).savingAccessControls =
      true;
    await settle();

    const savingPosition = saveButton.getBoundingClientRect();
    const spinner = dialog.querySelector('.save-progress .small-loader') as HTMLElement;

    expect(getComputedStyle(actionBar).gap).to.not.equal('normal');
    expect(savingPosition.x).to.equal(idlePosition.x);
    expect(savingPosition.y).to.equal(idlePosition.y);
    expect(spinner.offsetWidth).to.equal(16);
    expect(spinner.offsetHeight).to.equal(16);
  });

  it('ignores a repeated save while one is already in progress', async () => {
    const el = await mount();
    (el as unknown as { savingAccessControls: boolean }).savingAccessControls =
      true;

    expect(() =>
      (el as unknown as { save: () => void }).save()
    ).not.to.throw();
  });

  it('opens with the work in place', async () => {
    const el = await mount();
    expect(dialogIn(el), 'dialog rendered').to.not.equal(null);
    expect(el.Privileges?.length).to.equal(2);
  });

  it('survives Escape', async () => {
    const el = await mount();

    document.dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'Escape',
        bubbles: true,
        composed: true
      })
    );
    await settle();

    expect(el.dialogOpened, 'still open').to.equal(true);
    expect(el.Privileges?.length, 'unsaved rows kept').to.equal(2);
  });

  it('survives a click outside the dialog', async () => {
    const el = await mount();
    const overlay = overlayOf(el);
    expect(overlay, 'overlay found').to.not.equal(null);

    // The gesture Vaadin's outside-click detection listens for.
    for (const type of ['pointerdown', 'mousedown', 'mouseup', 'click']) {
      overlay?.dispatchEvent(
        new MouseEvent(type, { bubbles: true, composed: true })
      );
    }
    await settle();

    expect(el.dialogOpened, 'still open').to.equal(true);
    expect(el.Privileges?.length, 'unsaved rows kept').to.equal(2);
  });

  it('can still be closed by the Close button', async () => {
    // Opting out of Escape and outside-click makes Close the only way out, so
    // it is now load-bearing in a way it was not before. Nothing asserted it.
    const el = await mount();
    // Footer content is rendered into a slot, so it is a light-DOM descendant
    // of the dialog element rather than inside the overlay's shadow root.
    const dialog = el.shadowRoot?.querySelector('vaadin-dialog');
    const buttons = Array.from(
      dialog?.querySelectorAll('vaadin-button') ?? []
    ) as HTMLElement[];
    const close = buttons.find(b => b.textContent?.trim() === 'Close');

    expect(close, 'Close button rendered').to.not.equal(undefined);
    (close as HTMLElement).click();
    await settle();

    expect(el.dialogOpened, 'closes').to.equal(false);
  });

  it('still clears the form when it does close', async () => {
    // The reset is not removed — it is just reachable only through Close.
    const el = await mount();

    (el as unknown as { dialogOpened: boolean }).dialogOpened = false;
    await settle();

    expect(el.Privileges?.length, 'form cleared on a real close').to.equal(0);
  });
});
