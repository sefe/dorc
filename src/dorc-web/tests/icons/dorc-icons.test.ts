import { Iconset } from '@vaadin/icon/vaadin-iconset.js';
import { describe, expect, it } from 'vitest';
import {
  DORC_ICON_NAMES,
  dorcIcon,
  type DorcIconName
} from '../../src/icons/dorc-icons.js';
import { fixture, html } from '../_helpers';

const EXPECTED_CANONICAL_ICON_NAMES: readonly string[] = [
  'deploy',
  'monitor',
  'monitor-result',
  'projects',
  'environments',
  'environment-details',
  'environment-child',
  'environment-owner',
  'deployment-tree',
  'component-deployments',
  'bundles',
  'module-catalog',
  'scripts',
  'variables',
  'reference-data',
  'config-options',
  'audit-history',
  'environment-history',
  'analytics',
  'server',
  'container',
  'database',
  'terraform-state',
  'storage',
  'cloud',
  'daemons',
  'sql-port',
  'api-endpoint',
  'network',
  'control-center',
  'scan-discover',
  'password-reset',
  'users',
  'login-windows',
  'login-endur',
  'login-sql',
  'permissions-view',
  'permissions-manage',
  'permission-revoke',
  'access-control',
  'secret',
  'sql-role',
  'credentials',
  'unlock',
  'plus',
  'edit',
  'trash',
  'clear-value',
  'close',
  'decline',
  'copy',
  'clone',
  'search',
  'link',
  'unlink',
  'external-link',
  'pin',
  'tags',
  'list-select',
  'folder-open',
  'code',
  'file-log',
  'terraform-plan',
  'more-horizontal',
  'menu',
  'play',
  'plan-deploy',
  'resume',
  'pause',
  'stop',
  'cancel-request',
  'restart',
  'rerun',
  'refresh',
  'spinner',
  'check',
  'warning',
  'alert-circle',
  'info',
  'chevron-down',
  'chevron-up',
  'chevron-right',
  'arrow-right',
  'sun',
  'moon',
  'brand-github',
  'brand-azure-devops',
  'brand-azure',
  'brand-terraform',
  'brand-sqlserver',
  'brand-postgresql',
  'brand-clickhouse',
  'brand-cosmosdb',
  'brand-kafka'
];

describe('dorc:* canonical iconset registry', () => {
  it('registers <vaadin-iconset name="dorc"> with strictly the expected 94 canonical Tier-1 icon IDs', () => {
    const iconset = document.querySelector('vaadin-iconset[name="dorc"]');
    expect(iconset).not.toBeNull();
    expect(iconset?.getAttribute('size')).toBe('24');

    expect(EXPECTED_CANONICAL_ICON_NAMES).toHaveLength(94);
    expect(new Set(EXPECTED_CANONICAL_ICON_NAMES).size).toBe(94);
    expect([...DORC_ICON_NAMES]).toEqual(EXPECTED_CANONICAL_ICON_NAMES);

    // Vaadin 25 indexes querySelectorAll('[id]') — ensure no inner <path id="..."> leaks into the index
    const indexedIds = Array.from(iconset!.querySelectorAll('[id]')).map(el =>
      el.getAttribute('id')
    );
    expect(indexedIds).toEqual(EXPECTED_CANONICAL_ICON_NAMES);
  });

  it('provides non-empty, themeable SVG geometry and explicit viewBox for every dorc:* icon', () => {
    const iconset = document.querySelector('vaadin-iconset[name="dorc"]')!;
    const seenGlyphs = new Map<string, DorcIconName>();
    const acceptedSharedGlyphPairs = new Set([
      'deploy->plan-deploy',
      'restart->rerun'
    ]);

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
      const existing = seenGlyphs.get(signature);
      if (existing !== undefined) {
        expect(
          acceptedSharedGlyphPairs.has(`${existing}->${name}`),
          `unexpected duplicate glyph shared by dorc:${existing} and dorc:${name}`
        ).toBe(true);
      } else {
        seenGlyphs.set(signature, name);
      }

      const resolved = Iconset.getIconSvg(id);
      expect(
        resolved.svg,
        `Iconset.getIconSvg(${id}) returned empty`
      ).toBeTruthy();
    }
  });

  it.each([
    ['restart', 'refresh'],
    ['rerun', 'refresh'],
    ['copy', 'clone'],
    ['cancel-request', 'stop'],
    ['resume', 'play'],
    ['play', 'plan-deploy'],
    ['search', 'scan-discover'],
    ['decline', 'close'],
    ['permissions-manage', 'permissions-view'],
    ['permissions-view', 'users'],
    ['environment-details', 'server'],
    ['environment-details', 'config-options'],
    ['environment-history', 'audit-history'],
    ['trash', 'clear-value'],
    ['trash', 'permission-revoke'],
    ['unlink', 'permission-revoke'],
    ['access-control', 'secret'],
    ['sql-role', 'credentials'],
    ['database', 'terraform-state'],
    ['database', 'login-sql'],
    ['container', 'environments'],
    ['environments', 'component-deployments'],
    ['container', 'bundles'],
    ['sql-port', 'api-endpoint'],
    ['api-endpoint', 'network'],
    ['file-log', 'terraform-plan']
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
    expect(svg?.getAttribute('viewBox')).toBe('0 0 24 24');
    expect(svg?.querySelector('path')).not.toBeNull();
  });
});
