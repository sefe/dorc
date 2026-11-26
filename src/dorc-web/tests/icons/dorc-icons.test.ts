import { Iconset } from '@vaadin/icon/vaadin-iconset.js';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { MetadataApi } from '../../src/apis/dorc-api';
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
      expect(group?.getAttribute('viewBox')).toMatch(
        /^-?\d+(?:\.\d+)?\s+-?\d+(?:\.\d+)?\s+\d+(?:\.\d+)?\s+\d+(?:\.\d+)?$/
      );

      const innerMarkup = group!.innerHTML.trim();
      expect(innerMarkup.length, `empty glyph for ${id}`).toBeGreaterThan(0);
      expect(innerMarkup, `hardcoded hex color in ${id}`).not.toMatch(
        /#[0-9a-fA-F]{3,8}\b/
      );
      for (const attrMatch of innerMarkup.matchAll(
        /\b(?:fill|stroke|color|stop-color|flood-color|lighting-color)\s*=\s*["']([^"']+)["']/gi
      )) {
        expect(
          attrMatch[1].trim(),
          `non-monochrome SVG paint in ${id}`
        ).toMatch(/^(?:currentColor|none)$/i);
      }

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

  it('renders migrated dorc:* icons inside <vaadin-icon> with zero legacy iconsets registered', async () => {
    await import('../../src/helpers/database-type.js');

    expect(document.querySelector('vaadin-iconset[name="brand"]')).toBeNull();
    expect(document.querySelector('vaadin-iconset[name="vaadin"]')).toBeNull();
    expect(document.querySelector('vaadin-iconset[name="lumo"]')).toBeNull();

    const el = await fixture<HTMLElement>(
      html`<vaadin-icon icon="${dorcIcon('environment-owner')}"></vaadin-icon>`
    );

    const svg = el.shadowRoot?.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg?.getAttribute('viewBox')).toBe('0 0 24 24');
    expect(svg?.querySelector('path')).not.toBeNull();
  });

  it('normalizes optical live-area margins and centering across all 94 dorc:* icons on a 24x24 grid', () => {
    const iconset = document.querySelector('vaadin-iconset[name="dorc"]')!;
    const probeHost = document.createElement('div');
    probeHost.style.cssText =
      'position:fixed;left:-9999px;top:0;width:24px;height:24px;visibility:hidden';
    document.body.appendChild(probeHost);

    try {
      for (const name of DORC_ICON_NAMES) {
        const group = iconset.querySelector(`g#${CSS.escape(name)}`)!;
        const vbAttr = group.getAttribute('viewBox')!;
        const [minX, minY, vbW, vbH] = vbAttr.trim().split(/\s+/).map(Number);

        const svgNS = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(svgNS, 'svg');
        svg.setAttribute('viewBox', vbAttr);
        svg.setAttribute('width', '24');
        svg.setAttribute('height', '24');
        svg.innerHTML = group.innerHTML;
        probeHost.replaceChildren(svg);

        const bbox = svg.getBBox();
        const scale = Math.min(24 / vbW, 24 / vbH);
        const offsetX = (24 - vbW * scale) / 2;
        const offsetY = (24 - vbH * scale) / 2;

        const left = offsetX + (bbox.x - minX) * scale;
        const top = offsetY + (bbox.y - minY) * scale;
        const width = bbox.width * scale;
        const height = bbox.height * scale;
        const right = 24 - (left + width);
        const bottom = 24 - (top + height);
        const cx = left + width / 2;
        const cy = top + height / 2;

        // Every 24x24 icon must preserve >=2px optical padding (<=20px max span) and center cleanly
        expect(left, `dorc:${name} left margin`).toBeGreaterThanOrEqual(1.95);
        expect(top, `dorc:${name} top margin`).toBeGreaterThanOrEqual(1.95);
        expect(right, `dorc:${name} right margin`).toBeGreaterThanOrEqual(1.95);
        expect(bottom, `dorc:${name} bottom margin`).toBeGreaterThanOrEqual(
          1.95
        );
        expect(
          Math.max(width, height),
          `dorc:${name} max optical span`
        ).toBeLessThanOrEqual(20.05);
        expect(
          Math.max(width, height),
          `dorc:${name} min optical span`
        ).toBeGreaterThanOrEqual(9.5);
        expect(
          Math.abs(cx - 12),
          `dorc:${name} horizontal optical center (cx=${cx.toFixed(2)})`
        ).toBeLessThanOrEqual(0.85);
        expect(
          Math.abs(cy - 12),
          `dorc:${name} vertical optical center (cy=${cy.toFixed(2)})`
        ).toBeLessThanOrEqual(0.85);
      }
    } finally {
      probeHost.remove();
    }
  });
});

describe('Step 3 single-mechanism consolidation onto <vaadin-icon icon="dorc:*">', () => {
  it('replaces dorc-navbar local inline iconset with dorc:scripts and dorc:variables', async () => {
    const metadataSpy = vi
      .spyOn(MetadataApi.prototype, 'metadataGet')
      .mockReturnValue(of('1.0.0') as never);
    try {
      const { router } = await import('../../src/router/router.js');
      const { routes } = await import('../../src/router/routes.js');
      await (
        router as unknown as {
          setRoutes(r: unknown, skipRender?: boolean): Promise<unknown>;
        }
      ).setRoutes(routes, true);
      await import('../../src/components/dorc-navbar.js');

      const navbar = await fixture<HTMLElement>(
        html`<dorc-navbar></dorc-navbar>`
      );

      expect(navbar.shadowRoot?.querySelector('vaadin-iconset')).toBeNull();
      expect(
        navbar.shadowRoot?.querySelectorAll('vaadin-icon[icon^="inline:"]')
          .length
      ).toBe(0);
      expect(
        navbar.shadowRoot?.querySelectorAll('vaadin-icon[icon="dorc:scripts"]')
          .length
      ).toBe(2);
      expect(
        navbar.shadowRoot?.querySelectorAll(
          'vaadin-icon[icon="dorc:variables"]'
        ).length
      ).toBe(2);
    } finally {
      metadataSpy.mockRestore();
    }
  });

  it('renders GitHub and Azure DevOps project source icons via dorc:* without hardcoded hex fills', async () => {
    const { PageProjectsList } =
      await import('../../src/pages/page-projects-list.js');
    const { render } = await import('lit');

    const host = document.createElement('div');
    render(
      PageProjectsList.prototype._sourceControlTypeRenderer({
        SourceControlType: 'GitHub'
      } as never),
      host
    );
    const ghIcon = host.querySelector('vaadin-icon');
    expect(ghIcon?.getAttribute('icon')).toBe('dorc:brand-github');
    expect(host.querySelector('svg')).toBeNull();

    render(
      PageProjectsList.prototype._sourceControlTypeRenderer({
        SourceControlType: 'AzureDevOps'
      } as never),
      host
    );
    const adoIcon = host.querySelector('vaadin-icon');
    expect(adoIcon?.getAttribute('icon')).toBe('dorc:brand-azure-devops');
    expect(adoIcon?.getAttribute('style')).toContain('var(--dorc-link-color)');
    expect(host.innerHTML).not.toContain('#0078d4');
  });

  it('renders connection-status-indicator controls via dorc:pause, dorc:resume, and dorc:refresh', async () => {
    await import('../../src/components/connection-status-indicator.js');
    const indicator = await fixture<
      HTMLElement & { autoRefresh: boolean; updateComplete: Promise<boolean> }
    >(
      html`<connection-status-indicator
        state="Disconnected"
        .autoRefresh=${true}
      ></connection-status-indicator>`
    );

    expect(indicator.shadowRoot?.querySelector('svg')).toBeNull();
    expect(
      indicator.shadowRoot
        ?.querySelector('.strip-toggle vaadin-icon')
        ?.getAttribute('icon')
    ).toBe('dorc:pause');
    expect(
      indicator.shadowRoot
        ?.querySelector('.strip-refresh vaadin-icon')
        ?.getAttribute('icon')
    ).toBe('dorc:refresh');

    indicator.autoRefresh = false;
    await indicator.updateComplete;
    expect(
      indicator.shadowRoot
        ?.querySelector('.strip-toggle vaadin-icon')
        ?.getAttribute('icon')
    ).toBe('dorc:resume');
  });

  it('renders vector dorc:chevron-* icons in hegs-tree-node and hegs-json-viewer instead of CSS Unicode triangles', async () => {
    await import('../../src/components/deploy/component-tree/hegs-tree-node.js');
    await import('../../src/components/hegs-json-viewer.js');

    const parentNode = await fixture<
      HTMLElement & { open: boolean; updateComplete: Promise<boolean> }
    >(
      html`<hegs-tree-node
        .data=${{
          id: 1,
          name: 'Parent',
          hasParent: false,
          numOfChildren: 1,
          children: [
            {
              id: 2,
              name: 'Leaf',
              hasParent: true,
              numOfChildren: 0,
              children: []
            }
          ]
        }}
      ></hegs-tree-node>`
    );

    expect(
      parentNode.shadowRoot
        ?.querySelector('.node-preicon vaadin-icon')
        ?.getAttribute('icon')
    ).toBe('dorc:chevron-right');

    parentNode.open = true;
    await parentNode.updateComplete;
    expect(
      parentNode.shadowRoot
        ?.querySelector('.node-preicon vaadin-icon')
        ?.getAttribute('icon')
    ).toBe('dorc:chevron-down');

    const leafNode = await fixture<HTMLElement>(
      html`<hegs-tree-node
        .data=${{
          id: 2,
          name: 'Leaf',
          hasParent: true,
          numOfChildren: 0,
          children: []
        }}
      ></hegs-tree-node>`
    );
    expect(
      leafNode.shadowRoot?.querySelector('.node-preicon vaadin-icon')
    ).toBeNull();

    const viewer = await fixture<HTMLElement>(
      html`<hegs-json-viewer
        >{"nested":{"leaf":42},"flat":"ok"}</hegs-json-viewer
      >`
    );
    const nestedKey = viewer.shadowRoot?.querySelector(
      'li[data-path="nested"] > span[part="key"]'
    );
    const flatKey = viewer.shadowRoot?.querySelector(
      'li[data-path="flat"] > span[part="key"]'
    );

    expect(
      nestedKey
        ?.querySelector('vaadin-icon.collapse-icon')
        ?.getAttribute('icon')
    ).toBe('dorc:chevron-right');
    expect(nestedKey?.textContent?.trim()).toBe('nested:');
    expect(flatKey?.querySelector('vaadin-icon')).toBeNull();
    expect(flatKey?.textContent?.trim()).toBe('flat:');
  });
});

describe('Step 4 semantic de-conflation across split action controls', () => {
  it('separates dorc:rerun, dorc:restart, dorc:permission-revoke, and dorc:clear-value across action controls', async () => {
    await import('../../src/components/grid-button-groups/request-controls.js');
    await import('../../src/components/grid-button-groups/daemon-controls.js');
    await import('../../src/components/grid-button-groups/access-control-controls.js');
    await import('../../src/components/deploy/property-override-controls.js');
    await import('../../src/components/view-database-permissions.js');

    const reqControls = await fixture<HTMLElement>(
      html`<request-controls .canRestart=${true}></request-controls>`
    );
    expect(
      reqControls.shadowRoot
        ?.querySelector(
          'vaadin-button[aria-label="Restart Request"] vaadin-icon'
        )
        ?.getAttribute('icon')
    ).toBe('dorc:rerun');

    const daemonControls = await fixture<HTMLElement>(
      html`<daemon-controls></daemon-controls>`
    );
    expect(
      daemonControls.shadowRoot?.querySelector(
        'vaadin-icon[icon="dorc:restart"]'
      )
    ).not.toBeNull();

    const aclControls = await fixture<HTMLElement>(
      html`<access-control-controls></access-control-controls>`
    );
    expect(
      aclControls.shadowRoot
        ?.querySelector('vaadin-button[aria-label="Remove Access"] vaadin-icon')
        ?.getAttribute('icon')
    ).toBe('dorc:permission-revoke');

    const overrideControls = await fixture<HTMLElement>(
      html`<property-override-controls></property-override-controls>`
    );
    expect(
      overrideControls.shadowRoot
        ?.querySelector(
          'vaadin-button[aria-label="Remove Property Override"] vaadin-icon'
        )
        ?.getAttribute('icon')
    ).toBe('dorc:clear-value');

    const dbPerms = await fixture<
      HTMLElement & {
        userPermissionList: Array<{ Role: string }>;
        updateComplete: Promise<boolean>;
      }
    >(html`<view-database-permissions></view-database-permissions>`);
    dbPerms.userPermissionList = [{ Role: 'db_datareader' }];
    await dbPerms.updateComplete;

    const revokeBtn = dbPerms.shadowRoot?.querySelector(
      'vaadin-button[aria-label="Revoke permission"]'
    );
    expect(revokeBtn).not.toBeNull();
    expect(revokeBtn?.querySelector('vaadin-icon')?.getAttribute('icon')).toBe(
      'dorc:permission-revoke'
    );
    expect(
      revokeBtn?.querySelector('vaadin-tooltip')?.getAttribute('text')
    ).toBe('Revoke permission');
  });
});

describe('Step 5 legacy icon button & Polymer component retirement', () => {
  it('renders hegs-tree-node action menu with keyboard navigation, focus management, and action dispatch', async () => {
    await import('../../src/components/deploy/component-tree/hegs-tree-node.js');

    const treeNode = await fixture<
      HTMLElement & {
        actionsOpen: boolean;
        updateComplete: Promise<boolean>;
      }
    >(
      html`<hegs-tree-node
        .data=${{
          id: 42,
          name: 'Deploy Service',
          icon: '',
          open: false,
          hasParent: false,
          parentId: undefined,
          numOfChildren: 0,
          checked: false,
          indeterminate: false,
          children: []
        }}
        .actions=${[
          { label: 'Edit Step', event: 'edit-tree-step' },
          { label: 'Clone Step', event: 'clone-tree-step' }
        ]}
      ></hegs-tree-node>`
    );

    expect(treeNode.shadowRoot?.querySelector('paper-icon-button')).toBeNull();
    expect(treeNode.shadowRoot?.querySelector('paper-menu-button')).toBeNull();

    const trigger = treeNode.shadowRoot?.querySelector<HTMLElement>(
      '#actions vaadin-button.actions-trigger'
    );
    expect(trigger).not.toBeNull();
    expect(trigger?.getAttribute('theme')).toBe('icon small');
    expect(trigger?.querySelector('vaadin-icon')?.getAttribute('icon')).toBe(
      'dorc:edit'
    );

    trigger?.click();
    await treeNode.updateComplete;
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(treeNode.actionsOpen).toBe(true);

    const items = Array.from(
      treeNode.shadowRoot?.querySelectorAll<HTMLButtonElement>(
        '.actions-menu button.action-item[role="menuitem"]'
      ) ?? []
    );
    expect(items.length).toBe(2);
    expect(treeNode.shadowRoot?.activeElement).toBe(items[0]);

    const menu = treeNode.shadowRoot?.querySelector<HTMLElement>(
      '.actions-menu[role="menu"]'
    );
    menu?.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true })
    );
    expect(treeNode.shadowRoot?.activeElement).toBe(items[1]);

    menu?.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Home', bubbles: true })
    );
    expect(treeNode.shadowRoot?.activeElement).toBe(items[0]);

    menu?.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'End', bubbles: true })
    );
    expect(treeNode.shadowRoot?.activeElement).toBe(items[1]);

    menu?.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })
    );
    await treeNode.updateComplete;
    expect(treeNode.actionsOpen).toBe(false);

    // Re-open with ArrowUp on trigger -> focuses last item
    trigger?.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true })
    );
    await treeNode.updateComplete;
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(treeNode.actionsOpen).toBe(true);

    const reopenedItems = Array.from(
      treeNode.shadowRoot?.querySelectorAll<HTMLButtonElement>(
        '.actions-menu button.action-item[role="menuitem"]'
      ) ?? []
    );
    expect(treeNode.shadowRoot?.activeElement).toBe(reopenedItems[1]);

    let firedDetail: unknown = null;
    treeNode.addEventListener('edit-tree-step', ((e: CustomEvent) => {
      firedDetail = e.detail;
    }) as EventListener);

    expect(reopenedItems[0]?.textContent?.trim()).toBe('Edit Step');
    reopenedItems[0]?.click();
    await treeNode.updateComplete;

    expect(firedDetail).toEqual({ node: treeNode });
    expect(treeNode.actionsOpen).toBe(false);

    // Re-open and verify focusout with relatedTarget === null (clicking non-focusable body) closes menu
    trigger?.click();
    await treeNode.updateComplete;
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(treeNode.actionsOpen).toBe(true);

    const actionsContainer =
      treeNode.shadowRoot?.querySelector<HTMLElement>('#actions');
    actionsContainer?.dispatchEvent(
      new FocusEvent('focusout', { bubbles: true, relatedTarget: null })
    );
    await treeNode.updateComplete;
    expect(treeNode.actionsOpen).toBe(false);

    // Re-open and verify outside pointerdown on document.body also dismisses menu
    trigger?.click();
    await treeNode.updateComplete;
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(treeNode.actionsOpen).toBe(true);

    document.body.dispatchEvent(
      new PointerEvent('pointerdown', { bubbles: true, composed: true })
    );
    await treeNode.updateComplete;
    expect(treeNode.actionsOpen).toBe(false);
  });

  it('replaces unregistered <paper-listbox> and <paper-item> in edit-database-permissions and add-edit-database', async () => {
    await import('../../src/components/edit-database-permissions.js');
    await import('../../src/components/add-edit-database.js');

    const editPerms = await fixture<
      HTMLElement & {
        userPermissionList: Array<{
          Id: number;
          User: string;
          Role: string;
          DbType: string;
        }>;
        readonly: boolean;
        updateComplete: Promise<boolean>;
      }
    >(html`<edit-database-permissions></edit-database-permissions>`);
    editPerms.readonly = false;
    editPerms.userPermissionList = [
      { Id: 7, User: 'svc_deploy', Role: 'db_owner', DbType: 'MSSQL' }
    ];
    await editPerms.updateComplete;

    expect(editPerms.shadowRoot?.querySelector('paper-listbox')).toBeNull();
    expect(editPerms.shadowRoot?.querySelector('paper-item')).toBeNull();
    expect(
      editPerms.shadowRoot?.querySelector('ul.permissions-list')
    ).not.toBeNull();
    expect(
      editPerms.shadowRoot?.querySelectorAll('li.permission-row').length
    ).toBe(1);

    const revokeBtn = editPerms.shadowRoot?.querySelector(
      'vaadin-button[aria-label="Revoke permission"]'
    );
    expect(revokeBtn).not.toBeNull();
    expect(revokeBtn?.querySelector('vaadin-icon')?.getAttribute('icon')).toBe(
      'dorc:permission-revoke'
    );

    const addDb = await fixture<
      HTMLElement & { infoMessage: string; updateComplete: Promise<boolean> }
    >(html`<add-edit-database></add-edit-database>`);
    addDb.infoMessage = 'Database already exists';
    await addDb.updateComplete;

    expect(addDb.shadowRoot?.querySelector('paper-item')).toBeNull();
    const info = addDb.shadowRoot?.querySelector('.info-message');
    expect(info?.textContent?.trim()).toBe('Database already exists');
    expect(info?.getAttribute('style')).toContain(
      'var(--lumo-error-text-color)'
    );
  });
});
