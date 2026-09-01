// Test helpers for vitest browser-mode tests.
//
// We import chai's `expect` here so tests keep their BDD style
// (`expect(x).to.equal(y)`) rather than vitest's jest-style API,
// matching the existing assertion style in the suite.

import { html, render, type TemplateResult } from 'lit';
import { expect as chaiExpect } from 'chai';

export { html };
export const expect = chaiExpect;

// Containers registered by `fixture()` and cleaned up after each test by the
// global `afterEach` in tests/_setup.ts. Keeps document.body free of orphaned
// nodes that would otherwise dirty `getComputedStyle` measurements and
// re-register custom elements across test files.
const activeContainers = new Set<HTMLElement>();

export function _cleanupFixtures(): void {
  for (const container of activeContainers) {
    container.remove();
  }
  activeContainers.clear();
}

/**
 * Render a Lit template into the DOM and return the resulting element,
 * waiting for any LitElement `updateComplete` to settle.
 *
 * The container is tracked and removed in the global `afterEach`.
 *
 * `updateComplete` is typed as optional `Promise<unknown>` to accommodate both
 * LitElement hosts (resolves to a boolean) and plain DOM nodes (no property).
 * Tests can `await el.updateComplete` either way — `await undefined` resolves
 * to undefined.
 */
export type FixtureElement<T extends Element = HTMLElement> = T & {
  updateComplete?: Promise<unknown>;
};

export async function fixture<T extends Element = HTMLElement>(
  template: TemplateResult
): Promise<FixtureElement<T>> {
  const container = document.createElement('div');
  document.body.appendChild(container);
  activeContainers.add(container);
  render(template, container);

  const el = container.firstElementChild as T | null;
  if (!el) throw new Error('fixture(): template produced no element');

  const updateComplete = (el as unknown as { updateComplete?: Promise<unknown> })
    .updateComplete;
  if (updateComplete) await updateComplete;

  return el as FixtureElement<T>;
}

// ─── dialog helpers ───
//
// Observed structure in Vaadin 25.2.6 — verified against the running component,
// not assumed. This is NOT the Vaadin 23/24 model where overlays attach to
// document.body:
//
//   <host> #shadow
//     └ <vaadin-dialog>
//         ├ <vaadin-dialog-content slot="footer">  ← footer renderer output
//         ├ <vaadin-dialog-content>                ← content renderer output
//         └ #shadow → <vaadin-dialog-overlay opened>
//
// So renderer output is light-DOM children of the <vaadin-dialog> element, and
// the overlay lives in that element's own shadow root. Nothing is appended to
// document.body, which is why `document.querySelector('vaadin-dialog-overlay')`
// finds nothing and no overlay cleanup between tests is needed.

/** Lets the overlay transition and Lit's update queue settle. */
export const settle = (ms = 150): Promise<void> =>
  new Promise(resolve => setTimeout(resolve, ms));

/** The `<vaadin-dialog>` in a host's shadow root, optionally by id. */
export function dialogIn(host: Element, id?: string): HTMLElement | null {
  const selector = id ? `vaadin-dialog#${id}` : 'vaadin-dialog';
  return (host.shadowRoot?.querySelector(selector) ?? null) as HTMLElement | null;
}

/** True when the dialog's overlay is showing. */
export function isDialogOpen(dialog: HTMLElement | null): boolean {
  const overlay = dialog?.shadowRoot?.querySelector(
    'vaadin-dialog-overlay, vaadin-confirm-dialog-overlay'
  );
  return Boolean(overlay?.hasAttribute('opened'));
}

/** Queries the dialog's rendered content, which is its light-DOM children. */
export function inDialog<T extends Element = Element>(
  dialog: HTMLElement | null,
  selector: string
): T | null {
  return (dialog?.querySelector(selector) ?? null) as T | null;
}

/** Presses Escape. Vaadin's overlay listens on `document`. */
export function pressEscape(): void {
  document.dispatchEvent(
    new KeyboardEvent('keydown', {
      key: 'Escape',
      code: 'Escape',
      keyCode: 27,
      bubbles: true,
      composed: true,
      cancelable: true
    })
  );
}

/**
 * Clicks outside the dialog. Vaadin decides "outside" from the pointer-down
 * target, and its backdrop lives in the overlay's shadow root.
 */
export function clickOutside(dialog: HTMLElement | null): void {
  const overlay = dialog?.shadowRoot?.querySelector('vaadin-dialog-overlay');
  const target =
    (overlay?.shadowRoot?.querySelector('[part="backdrop"]') as HTMLElement | null) ??
    (overlay as HTMLElement | null) ??
    document.body;
  for (const type of ['pointerdown', 'mousedown', 'click']) {
    target.dispatchEvent(
      new MouseEvent(type, { bubbles: true, composed: true, cancelable: true })
    );
  }
}
