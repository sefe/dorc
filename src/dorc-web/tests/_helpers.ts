// Test helpers for vitest browser-mode tests.
//
// We import chai's `expect` here so tests keep their BDD style
// (`expect(x).to.equal(y)`) rather than vitest's jest-style API,
// matching the existing assertion style in the suite.

import { html, render, type TemplateResult } from 'lit';
import { expect as chaiExpect } from 'chai';

export { html };
export const expect = chaiExpect;

/**
 * Render a Lit template into the DOM and return the resulting element,
 * waiting for any LitElement `updateComplete` to settle.
 *
 * Mirrors the subset of @open-wc/testing's `fixture` we actually use.
 */
export async function fixture<T extends Element = HTMLElement>(
  template: TemplateResult
): Promise<T> {
  const container = document.createElement('div');
  document.body.appendChild(container);
  render(template, container);

  const el = container.firstElementChild as T | null;
  if (!el) throw new Error('fixture(): template produced no element');

  const updateComplete = (el as unknown as { updateComplete?: Promise<unknown> })
    .updateComplete;
  if (updateComplete) await updateComplete;

  return el;
}
