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

// ─── Theme switching ───
// The app sets theme via `theme="dark"` on <html> (see src/theme/theme-manager.ts);
// src/theme/dorc-tokens.css keys its dark block off the same attribute. Tests that
// assert colour must run in both themes, so this flips it and _setup.ts's afterEach
// restores light.

export type DorcTheme = 'light' | 'dark';

export function setTheme(theme: DorcTheme): void {
  if (theme === 'dark') {
    document.documentElement.setAttribute('theme', 'dark');
  } else {
    document.documentElement.removeAttribute('theme');
  }
}

export function resetTheme(): void {
  setTheme('light');
}

// ─── Contrast ───
// Computes a real WCAG contrast ratio against the *painted* backdrop rather than
// the element's own (usually transparent) background.
//
// Three rules make this trustworthy, all of which a naive implementation gets wrong:
//   1. The backdrop is found by walking ancestors ACROSS shadow boundaries — an
//      element inside three nested shadow roots still inherits the page background.
//   2. A colour with alpha < 1 is composited over that backdrop BEFORE the
//      luminance formula is applied. Lumo's secondary text tokens resolve to
//      hsla()/rgba() values, so skipping this silently mis-measures them.
//   3. If no opaque backdrop is found, this THROWS. It must never assume white —
//      that is precisely the vacuous pass this helper exists to prevent.

interface Rgba {
  r: number;
  g: number;
  b: number;
  a: number;
}

function parseColor(value: string): Rgba | null {
  const m = value.match(
    /rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:[,/\s]+([\d.]+%?))?\s*\)/
  );
  if (!m) return null;
  let a = 1;
  if (m[4] !== undefined) {
    a = m[4].endsWith('%') ? parseFloat(m[4]) / 100 : parseFloat(m[4]);
  }
  return { r: +m[1], g: +m[2], b: +m[3], a };
}

function composite(fg: Rgba, bg: Rgba): Rgba {
  return {
    r: fg.r * fg.a + bg.r * (1 - fg.a),
    g: fg.g * fg.a + bg.g * (1 - fg.a),
    b: fg.b * fg.a + bg.b * (1 - fg.a),
    a: 1
  };
}

function relativeLuminance({ r, g, b }: Rgba): number {
  const channel = (v: number) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/** Walks up through shadow roots to the first ancestor painting an opaque background. */
export function paintedBackdrop(el: Element): Rgba {
  let node: Node | null = el;
  while (node) {
    if (node instanceof Element) {
      const parsed = parseColor(getComputedStyle(node).backgroundColor);
      if (parsed && parsed.a === 1) return parsed;
    }
    const parent: Node | null = node.parentNode;
    node =
      parent instanceof ShadowRoot
        ? parent.host
        : parent ?? (node as Element).ownerDocument?.defaultView?.frameElement ?? null;
    if (node === node?.parentNode) break;
  }
  throw new Error(
    'paintedBackdrop: no opaque ancestor background found. Refusing to assume white — ' +
      'an assumed backdrop is how a contrast test passes while the app fails.'
  );
}

/** WCAG contrast ratio of `color` (any CSS colour string) against the element's painted backdrop. */
export function contrastRatio(el: Element, color: string): number {
  const fg = parseColor(color);
  if (!fg) throw new Error(`contrastRatio: could not parse colour "${color}"`);
  const bg = paintedBackdrop(el);
  const solid = fg.a < 1 ? composite(fg, bg) : fg;
  const [l1, l2] = [relativeLuminance(solid), relativeLuminance(bg)].sort(
    (a, b) => b - a
  );
  return (l1 + 0.05) / (l2 + 0.05);
}

/** WCAG 1.4.3 threshold for text: 3:1 for large text (>=24px, or >=18.66px bold), else 4.5:1. */
export function textContrastThreshold(el: Element): number {
  const cs = getComputedStyle(el);
  const px = parseFloat(cs.fontSize);
  const weight = parseInt(cs.fontWeight, 10) || 400;
  const isLarge = px >= 24 || (px >= 18.66 && weight >= 700);
  return isLarge ? 3 : 4.5;
}
