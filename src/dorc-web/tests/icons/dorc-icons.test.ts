import { Iconset } from '@vaadin/icon/vaadin-iconset.js';
import { describe, expect, it } from 'vitest';
import {
  DORC_ICON_NAMES,
  dorcIcon,
  type DorcIconName
} from '../../src/icons/dorc-icons.js';
import { fixture, html } from '../_helpers';

describe('dorc:* canonical iconset registry', () => {
  it('registers <vaadin-iconset name="dorc"> with strictly the canonical Tier-1 icon IDs', () => {
    const iconset = document.querySelector('vaadin-iconset[name="dorc"]');
    expect(iconset).not.toBeNull();
    expect(iconset?.getAttribute('size')).toBe('24');

    // Vaadin 25 indexes querySelectorAll('[id]') — ensure no inner <path id="..."> leaks into the index
    const indexedIds = Array.from(iconset!.querySelectorAll('[id]')).map(el =>
      el.getAttribute('id')
    );
    expect(new Set(DORC_ICON_NAMES).size).toBe(94);
    expect(indexedIds).toEqual([...DORC_ICON_NAMES]);
  });

  it('provides non-empty, themeable SVG geometry and explicit viewBox for every dorc:* icon', () => {
    const iconset = document.querySelector('vaadin-iconset[name="dorc"]')!;
    const seenGlyphs = new Map<string, DorcIconName>();

    for (const name of DORC_ICON_NAMES) {
      const id = dorcIcon(name);
      expect(id).toBe(`dorc:${name}`);

      const group = iconset.querySelector(`g#${CSS.escape(name)}`);
      expect(group, `missing <g id="${name}">`).not.toBeNull();
      expect(group?.getAttribute('viewBox')).toMatch(/^\d+\s+\d+\s+\d+\s+\d+$/);

      const innerMarkup = group!.innerHTML.trim();
      expect(innerMarkup.length, `empty glyph for ${id}`).toBeGreaterThan(0);
      expect(innerMarkup, `hardcoded hex color in ${id}`).not.toMatch(
        /#[0-9a-fA-F]{3,8}\b/
      );

      const signature = `${group!.getAttribute('viewBox')}|${innerMarkup}`;
      expect(
        seenGlyphs.get(signature),
        `duplicate glyph shared by dorc:${name} and dorc:${seenGlyphs.get(signature)}`
      ).toBeUndefined();
      seenGlyphs.set(signature, name);

      const resolved = Iconset.getIconSvg(id);
      expect(
        resolved.svg,
        `Iconset.getIconSvg(${id}) returned empty`
      ).toBeTruthy();
    }
  });

  it.each([
    ['restart', 'refresh'],
    ['cancel-request', 'stop'],
    ['resume', 'play'],
    ['permissions-manage', 'permissions-view'],
    ['permissions-view', 'users'],
    ['environment-details', 'server'],
    ['environment-history', 'audit-history']
  ] as const)(
    'keeps dorc:%s visually distinct from dorc:%s',
    (first, second) => {
      const iconset = document.querySelector('vaadin-iconset[name="dorc"]')!;
      const firstGroup = iconset.querySelector(`g#${first}`)!;
      const secondGroup = iconset.querySelector(`g#${second}`)!;

      expect(firstGroup.innerHTML).not.toBe(secondGroup.innerHTML);
    }
  );

  it('renders migrated Step 2 dorc:* icons inside <vaadin-icon>', async () => {
    const el = await fixture<HTMLElement>(
      html`<vaadin-icon icon="${dorcIcon('environment-owner')}"></vaadin-icon>`
    );

    const svg = el.shadowRoot?.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg?.getAttribute('viewBox')).toBe('0 0 32 32');
    expect(svg?.querySelector('path')).not.toBeNull();
  });
});
