import { expect, fixture, html } from '../_helpers';

// P3 — the environment detail page the drawer shortcuts link into.

interface ReadyPage extends HTMLElement {
  updateComplete: Promise<unknown>;
  environmentName: string;
}

describe('P3: environment page', () => {
  beforeAll(async () => {
    const { router } = await import('../../src/router/router.js');
    const { routes } = await import('../../src/router/routes.js');
    await (
      router as unknown as {
        setRoutes(r: unknown, skipRender?: boolean): Promise<unknown>;
      }
    ).setRoutes(routes, true);
    await import('../../src/pages/page-environment.js');
  });

  const mount = async () =>
    (await fixture(
      html`<page-environment></page-environment>`
    )) as unknown as ReadyPage;

  // ─── D-21 ───────────────────────────────────────────────────────────────
  describe('SC-21: the header is not a data table', () => {
    it('uses a flex header, not <table>', async () => {
      const page = await mount();
      const root = page.shadowRoot!;

      expect(
        root.querySelector('table'),
        'a layout <table> is announced as "table, 1 row, 3 columns"'
      ).to.not.exist;
      expect(root.querySelector('.env-header')).to.exist;
    });

    it('announces the environment name, which only arrives asynchronously', async () => {
      const page = await mount();
      const heading = page.shadowRoot!.querySelector('h2')!;

      // Empty on first paint — the name lands when the API call resolves, so
      // without a live region it is never announced.
      expect(heading.getAttribute('aria-live')).to.equal('polite');
    });

    it('gives the loading indicator a status role', async () => {
      const page = await mount();
      const loader = page.shadowRoot!.querySelector('.small-loader');

      // `loading` defaults true, so the indicator is present on first paint.
      expect(loader, 'loader renders while loading').to.exist;
      expect(loader!.getAttribute('role')).to.equal('status');
      expect(loader!.getAttribute('aria-label')).to.match(/loading/i);
    });
  });

  // ─── D-22 ───────────────────────────────────────────────────────────────
  describe('SC-21: the loader is themed', () => {
    it('uses theme tokens rather than hardcoded greys', async () => {
      const { PageEnvironment } = await import(
        '../../src/pages/page-environment.js'
      );
      const css = (
        PageEnvironment as unknown as {
          styles: { cssText: string } | Array<{ cssText: string }>;
        }
      ).styles;
      const text = Array.isArray(css)
        ? css.map(s => s.cssText).join('\n')
        : css.cssText;
      // Strip comments — the fix is described in one, and we are asserting on
      // declarations, not prose.
      const declarations = text.replace(/\/\*[\s\S]*?\*\//g, '');

      // #f3f3f3 rendered as a glaring near-white ring on the dark theme's #1e1e1e.
      expect(declarations, 'no hardcoded loader colours').to.not.match(
        /#f3f3f3|#3498db/
      );
      expect(declarations).to.match(/--dorc-border-color/);
      expect(declarations).to.match(/prefers-reduced-motion/);
    });
  });

  // ─── D-36 ───────────────────────────────────────────────────────────────
  describe('SC-21: not-found is not a blank page', () => {
    it('renders an announced empty state with a way back', async () => {
      const page = await mount();
      (page as unknown as { notFound: boolean }).notFound = true;
      await page.updateComplete;

      const root = page.shadowRoot!;
      const alert = root.querySelector('[role="alert"]');
      expect(alert, 'must announce, not render nothing').to.exist;
      expect(alert!.textContent).to.match(/not found/i);
      expect(
        root.querySelector('a[href="/environments"]'),
        'offers a route out'
      ).to.exist;
    });
  });

  // ─── D-26 ───────────────────────────────────────────────────────────────
  describe('SC-22: tab list and route mapping come from one source', () => {
    it('renders a tab for every routable name, with no hidden-tab gap', async () => {
      const page = await mount();
      const names = (page as unknown as { tabNames: string[] }).tabNames;
      const rendered = page.shadowRoot!.querySelectorAll('vaadin-tab');

      // Previously `tabNames` was the full enum while the Users tab was dropped
      // for non-Endur environments, so the indexed list and the rendered list
      // disagreed — surviving only because Users happened to be declared last.
      expect(rendered.length, 'rendered tabs match the indexed list').to.equal(
        names.length
      );
    });

    it('no longer gates a tab on the environment name containing "endur"', async () => {
      const { PageEnvironment } = await import(
        '../../src/pages/page-environment.js'
      );
      const src = PageEnvironment.prototype.convertUriToHuman.toString();
      expect(src, 'the endur hard-coding is gone').to.not.match(/endur/i);
    });
  });
});
