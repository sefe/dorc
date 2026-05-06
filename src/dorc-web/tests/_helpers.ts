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
 * The returned type asserts a LitElement-style `updateComplete` so tests can
 * await further updates after dispatching events. The body still guards
 * against the property being absent for non-Lit hosts.
 */
export type FixtureElement<T extends Element = HTMLElement> = T & {
  updateComplete: Promise<void>;
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
