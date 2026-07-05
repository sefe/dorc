import { expect, fixture, html } from '../_helpers';
import '../../src/components/environment-card.js';
import type { EnvironmentCard } from '../../src/components/environment-card.js';
import '../../src/components/project-card.js';
import type { ProjectCard } from '../../src/components/project-card.js';

describe('Touch target structure', () => {
  describe('Environment card buttons', () => {
    const mockEnvironment = {
      EnvironmentId: 1,
      EnvironmentName: 'Test Env',
      Details: {
        Description: 'desc',
        EnvironmentOwner: 'owner',
      },
    };

    it('should render icon buttons with theme="icon" for global style targeting', async () => {
      const el = await fixture<EnvironmentCard>(html`
        <environment-card .environment="${mockEnvironment}"></environment-card>
      `);
      await el.updateComplete;

      const buttons = el.shadowRoot!.querySelectorAll('vaadin-button[theme="icon"]');
      expect(buttons.length).to.equal(4, 'Should have 4 icon-themed buttons');
    });

    it('should not use reduced padding that would shrink touch targets', async () => {
      const el = await fixture<EnvironmentCard>(html`
        <environment-card .environment="${mockEnvironment}"></environment-card>
      `);
      await el.updateComplete;

      // Verify the card doesn't use padding: 2px on vaadin-button (old value)
      const styles = (el.constructor as typeof EnvironmentCard).styles;
      const cssText = Array.isArray(styles)
        ? styles.map((s: any) => s.cssText).join('')
        : (styles as any).cssText || '';

      // Should not have padding: 2px for vaadin-button (the old problematic value)
      expect(cssText).to.not.include('padding: 2px');
    });
  });

  describe('Project card buttons', () => {
    const mockProject = {
      ProjectId: 1,
      ProjectName: 'Test Project',
      ProjectDescription: 'desc',
    };

    it('should render icon button with theme="icon" for global style targeting', async () => {
      const el = await fixture<ProjectCard>(html`
        <project-card .project="${mockProject}"></project-card>
      `);
      await el.updateComplete;

      const buttons = el.shadowRoot!.querySelectorAll('vaadin-button[theme="icon"]');
      expect(buttons.length).to.equal(1, 'Should have 1 icon-themed button');
    });
  });

  describe('Style registration for icon buttons', () => {
    // The 44×44 touch-target floor is gated behind @media (max-width: 768px)
    // so desktop compact grids (theme="icon small", --lumo-button-size: 28px)
    // aren't blown up. Assert (a) the rule lives INSIDE a max-width:768px
    // media block and (b) no rule sets the same min-* at the top level —
    // otherwise a future change could lift the rule out of the media query
    // and we'd silently re-introduce the regression.
    it('should register a mobile-only 44x44 min size for vaadin-button[theme~="icon"]', async () => {
      await import('../../src/router/style-registrations.js');
      await import('@vaadin/button');

      const button = await fixture<HTMLElement>(html`
        <vaadin-button theme="icon" aria-label="probe"></vaadin-button>
      `);
      const sheets = button.shadowRoot?.adoptedStyleSheets ?? [];
      const allRules = sheets.flatMap(sheet => Array.from(sheet.cssRules));

      const mobileMediaRule = allRules.find(
        (rule): rule is CSSMediaRule =>
          rule instanceof CSSMediaRule &&
          /max-width:\s*768px/.test(rule.conditionText)
      );
      expect(mobileMediaRule, 'mobile media query block').to.exist;

      const nestedCss = Array.from(mobileMediaRule!.cssRules)
        .map(r => r.cssText)
        .join('\n');
      expect(nestedCss).to.match(
        /min-width:\s*44px/,
        'min-width: 44px should be nested inside @media (max-width: 768px)'
      );
      expect(nestedCss).to.match(
        /min-height:\s*44px/,
        'min-height: 44px should be nested inside @media (max-width: 768px)'
      );

      // Guard the regression that round-5 fixed: no top-level (non-media)
      // rule should set the 44px floor on icon buttons.
      const topLevelStyleRules = allRules.filter(
        (rule): rule is CSSStyleRule => rule instanceof CSSStyleRule
      );
      for (const rule of topLevelStyleRules) {
        if (/theme.*icon/.test(rule.selectorText)) {
          expect(rule.style.minWidth).to.not.equal('44px');
          expect(rule.style.minHeight).to.not.equal('44px');
        }
      }
    });
  });

  describe('Database list tag/env buttons', () => {
    it('should verify tag and env button styles exist', async () => {
      // Import the page to check its styles
      const module = await import('../../src/pages/page-databases-list.js');
      const PageDatabasesList = module.PageDatabasesList;

      const styles = PageDatabasesList.styles;
      const cssText = Array.isArray(styles)
        ? styles.map((s: any) => s.cssText).join('')
        : (styles as any).cssText || '';

      // Tag and env buttons should have display: inline-block and padding
      expect(cssText).to.include('.tag',
        'Should have .tag class styles');
      expect(cssText).to.include('.env',
        'Should have .env class styles');
      expect(cssText).to.include('display: inline-block',
        'Tag/env buttons should be inline-block');
    });
  });
});
