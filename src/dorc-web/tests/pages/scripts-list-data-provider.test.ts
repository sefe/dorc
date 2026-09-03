import { describe, it, expect, vi } from 'vitest';
import { of } from 'rxjs';

// The same defect as env-variables-edit-mode guards, reached by a different
// route. Vaadin's `_dataProviderChanged` calls `clearCache()` whenever the
// provider's identity changes (vaadin-grid-data-provider-mixin.js:362-367), so
// an inline arrow in the template — a new function on every host render — makes
// the grid throw its pages away and re-query.
//
// This branch promoted `userRoles` to `@state()` so two column dependency
// arrays could see it, and the roles callback resolves after first paint. For
// an admin nothing changed, because `isAdmin` was already reactive. For
// everyone else that callback used to trigger no re-render at all and now
// triggers one — so the grid populated, blanked, and re-queried the paged API
// once roles landed, losing scroll position on the way.
//
// The invariant is the one Vaadin actually keys on: the provider keeps its
// identity across a host re-render.
//
// Its own file, with the API mocked. Adding it to the sibling guard made that
// file flaky — this page fires several requests on connect, and the unhandled
// rejections killed the whole file intermittently.

vi.mock('../../src/apis/dorc-api', async importOriginal => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    RefDataScriptsApi: class {
      refDataScriptsGet = () => of([]);
      refDataScriptsPagedGet = () => of({ Items: [], TotalItems: 0 });
    },
    PowerShellVersionsApi: class {
      powerShellVersionsGet = () => of([]);
    }
  };
});

await import('../../src/pages/page-scripts-list');

const settle = () => new Promise(r => setTimeout(r, 200));

const providerOf = (el: HTMLElement) =>
  (
    el.shadowRoot?.querySelector('vaadin-grid') as
      (HTMLElement & { dataProvider?: unknown }) | null
  )?.dataProvider;

describe('page-scripts-list keeps its data provider', () => {
  it('survives the roles callback landing', async () => {
    const el = document.createElement('page-scripts-list') as HTMLElement & {
      updateComplete: Promise<unknown>;
    };
    document.body.appendChild(el);
    await el.updateComplete;
    await settle();

    const before = providerOf(el);
    expect(before, 'grid has a data provider').to.not.equal(undefined);

    // Exactly what the roles callback writes for a non-admin.
    (el as unknown as { userRoles: string[] }).userRoles = ['SomeRole'];
    await el.updateComplete;
    await settle();

    expect(
      providerOf(el),
      'same provider, so the grid keeps its pages'
    ).to.equal(before);

    el.remove();
  });
});
