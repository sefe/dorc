import { describe, it, expect } from 'vitest';
import '../../src/components/bundle-editor-dialog';

// The dialog's content used to be a closure declared inside render() bound with
// an empty dependency array. LitRendererDirective.update() dirty-checks the
// dependency array and returns early when nothing changed — it never looks at
// the renderer's identity — so the first closure was pinned and runRenderer()
// was never called again. Anything that changed the dialog's inputs while it
// was open, which is what `updateBundleRequest()` exists to do, could not
// repaint the form.

type Dialog = HTMLElement & {
  open: boolean;
  projects: unknown[] | null;
  existingBundleNames: string[];
  bundleRequest: { BundleName?: string };
  openEdit(
    bundle: { BundleName?: string },
    projects?: unknown[] | null,
    names?: string[]
  ): void;
  updateBundleRequest(bundle: { BundleName?: string }): void;
};

const settle = async () => {
  for (let i = 0; i < 4; i++) {
    await new Promise(r => requestAnimationFrame(() => r(null)));
  }
};

// Vaadin 25 nests the overlay inside the <vaadin-dialog> element's own shadow
// root, so the rendered content is not reachable from the document.
type Form = HTMLElement & { bundleRequest?: { BundleName?: string } };

const formIn = (dialog: Dialog): Form | null =>
  (dialog.shadowRoot?.querySelector('bundle-editor-form') as Form | null) ??
  (document.querySelector('bundle-editor-form') as Form | null);

describe('bundle editor dialog content', () => {
  it('repaints the form when the bundle changes while open', async () => {
    const dialog = document.createElement('bundle-editor-dialog') as Dialog;
    document.body.appendChild(dialog);
    await settle();

    dialog.openEdit({ BundleName: 'First' }, [], []);
    await settle();

    const form = formIn(dialog);
    expect(form, 'the dialog rendered its form').not.toBe(null);
    expect(form?.bundleRequest?.BundleName).toBe('First');

    // The form pushes an updated bundle back up while the dialog is open.
    dialog.updateBundleRequest({ BundleName: 'Second' });
    await settle();

    expect(
      formIn(dialog)?.bundleRequest?.BundleName,
      'content follows the new bundle'
    ).toBe('Second');

    dialog.open = false;
    await settle();
    dialog.remove();
  });
});
