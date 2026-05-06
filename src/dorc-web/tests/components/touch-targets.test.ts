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
    it('should give vaadin-button[theme="icon"] a 44x44 minimum touch target', async () => {
      await import('../../src/router/style-registrations.js');
      await import('@vaadin/button');

      const button = await fixture<HTMLElement>(html`
        <vaadin-button theme="icon" aria-label="probe"></vaadin-button>
      `);
      const computed = getComputedStyle(button);
      expect(parseFloat(computed.minWidth)).to.be.at.least(44);
      expect(parseFloat(computed.minHeight)).to.be.at.least(44);
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
