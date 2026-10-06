import {
  contrastRatio,
  expect,
  fixture,
  html,
  paintedBackdrop,
  setTheme,
  textContrastThreshold
} from './_helpers';

// SC-30 — the prerequisite every contrast criterion depends on.
//
// The --dorc-* tokens used to live only in an inline <style> in index.html, which
// vitest's tester HTML does not load. A contrast test written against them would
// resolve var(--dorc-bg-primary) to nothing, fall back to transparent, and pass
// in BOTH themes while the app failed. These tests assert the tokens are actually
// present and theme-switchable, so a failure here invalidates every contrast
// assertion downstream rather than letting them quietly mean nothing.

const token = (name: string) =>
  getComputedStyle(document.documentElement).getPropertyValue(name).trim();

describe('SC-30: theme tokens are resolvable in the test document', () => {
  it('resolves the core --dorc-* tokens to real values', () => {
    for (const name of [
      '--dorc-bg-primary',
      '--dorc-bg-secondary',
      '--dorc-text-primary',
      '--dorc-text-secondary',
      '--dorc-border-color',
      '--dorc-link-color'
    ]) {
      expect(token(name), `${name} must resolve`).to.not.equal('');
    }
  });

  it('switches values when the theme flips', () => {
    setTheme('light');
    const lightBg = token('--dorc-bg-primary');
    setTheme('dark');
    const darkBg = token('--dorc-bg-primary');

    expect(lightBg).to.not.equal('');
    expect(darkBg).to.not.equal('');
    expect(darkBg, 'dark theme must override the light value').to.not.equal(
      lightBg
    );
  });
});

describe('SC-30: the contrast helper is trustworthy', () => {
  it('measures a known ratio correctly', async () => {
    // #747f8d on #f5f6f8 is the D-23 failure: 3.76:1, short of 1.4.3's 4.5:1.
    const el = await fixture(
      html`<div style="background: #f5f6f8"><span>probe</span></div>`
    );
    const span = el.querySelector('span')!;
    const ratio = contrastRatio(span, 'rgb(116, 127, 141)');

    expect(ratio).to.be.closeTo(3.76, 0.05);
    expect(ratio).to.be.below(textContrastThreshold(span));
  });

  it('composites a translucent colour over the backdrop before measuring', async () => {
    const el = await fixture(
      html`<div style="background: #ffffff"><span>probe</span></div>`
    );
    const span = el.querySelector('span')!;

    // Black at 50% over white is mid-grey (~#808080), not black. A helper that
    // ignored alpha would report 21:1 instead of ~3.9:1.
    const ratio = contrastRatio(span, 'rgba(0, 0, 0, 0.5)');
    expect(ratio).to.be.below(5);
    expect(ratio).to.be.above(3);
  });

  it('finds the backdrop across a shadow boundary', async () => {
    const host = await fixture(
      html`<div style="background: #ffffff"><div id="inner"></div></div>`
    );
    const inner = host.querySelector('#inner') as HTMLElement;
    const root = inner.attachShadow({ mode: 'open' });
    root.innerHTML = '<span>probe</span>';
    const span = root.querySelector('span')!;

    const bg = paintedBackdrop(span);
    expect(bg).to.deep.equal({ r: 255, g: 255, b: 255, a: 1 });
  });

  it('THROWS rather than assuming white when no opaque backdrop exists', () => {
    // The anti-vacuity rule. An assumed backdrop is exactly how a contrast test
    // passes while the app fails, so this must be an error, not a default.
    const orphan = document.createElement('span');

    expect(() => paintedBackdrop(orphan)).to.throw(/Refusing to assume white/);
  });
});
