import { describe, it, expect, vi } from 'vitest';
import { of } from 'rxjs';

// The Value column header carries the AND/OR toggle, and its label is derived:
// `useAndFilter ? 'Search Filter: AND' : 'Search Filter: OR'`. The checkbox's
// own tick is moved by the browser, so it always looks right — only the label
// tells the user which logic is actually in force, and only a repaint updates
// it.
//
// This grid is the clean single-column case the config-values one is not: every
// other array here is `[]`, so nothing else can trigger the grid-wide
// `requestContentUpdate()` that `runRenderer` performs. Drop `useAndFilter`
// from this array and the label freezes on 'Search Filter: AND' while the
// request goes out with `useAndLogic: false` — the page then states the
// opposite of what it is doing, on a filter that decides whether results are
// intersected or unioned.

const { putSpy, requestedLogic } = vi.hoisted(() => {
  const requestedLogic: (boolean | undefined)[] = [];
  return {
    requestedLogic,
    putSpy: vi.fn((request: { useAndLogic?: boolean }) => {
      requestedLogic.push(request.useAndLogic);
      return of({ Items: [], TotalItems: 0 });
    })
  };
});

vi.mock('../../src/apis/dorc-api', async importOriginal => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    PropertyValuesAuditApi: class {
      propertyValuesAuditPut = putSpy;
    }
  };
});

await import('../../src/pages/page-variables-audit');

const settle = () => new Promise(r => setTimeout(r, 250));

const filterCheckbox = (el: HTMLElement) =>
  el.shadowRoot
    ?.querySelector('vaadin-grid')
    ?.querySelector('#filter-checkbox') as
    (HTMLElement & { checked: boolean; label: string }) | null;

describe('variables audit AND/OR filter label', () => {
  it('follows the toggle instead of freezing on the initial value', async () => {
    const el = document.createElement('page-variables-audit') as HTMLElement & {
      useAndFilter: boolean;
      updateComplete: Promise<unknown>;
    };
    document.body.appendChild(el);
    await el.updateComplete;
    await settle();

    const checkbox = filterCheckbox(el);
    expect(checkbox, 'the header rendered its toggle').to.not.equal(null);
    expect(el.useAndFilter, 'AND by default').to.equal(true);
    expect(checkbox?.label, 'and the label says so').to.equal(
      'Search Filter: AND'
    );

    // The real gesture. The checkbox is ticked *off* for AND, so clicking it
    // switches to OR.
    checkbox?.click();
    await el.updateComplete;
    await settle();

    expect(el.useAndFilter, 'the toggle took effect').to.equal(false);
    expect(
      requestedLogic.includes(false),
      'and the request went out with OR logic'
    ).to.equal(true);
    expect(
      filterCheckbox(el)?.label,
      'so the label must have repainted to match'
    ).to.equal('Search Filter: OR');

    el.remove();
  });
});
