import { expect } from '../_helpers';
import '../../src/components/environment-tabs/env-variables';
import '../../src/pages/page-variables-value-lookup';

// Entering edit mode on a variable is a local UI toggle: the control dispatches
// `editing-started`, the host records `_editingValueId`, and the cell repaints.
//
// This branch promoted `_editingValueId` to `@state()` — it was a plain field,
// repainted by a `grid.requestContentUpdate()` this branch deleted. So pressing
// Edit now re-renders the host, and the grid's data provider was an inline arrow
// in the template: every host render handed Vaadin a *new* function. Vaadin's
// `_dataProviderChanged` calls `clearCache()` whenever the provider's identity
// changes (vaadin-grid-data-provider-mixin.js:362-367), so the grid threw its
// cache away and re-queried the API each time the user pressed Edit or Cancel —
// a flash and a wasted paged request for a local toggle.
//
// The invariant is therefore: the data provider must keep its identity across a
// host re-render. That is what this asserts, rather than counting requests,
// because it is the property Vaadin actually keys on.

const settle = () => new Promise(r => setTimeout(r, 150));

const providerOf = (el: HTMLElement) =>
  (
    el.shadowRoot?.querySelector('vaadin-grid') as
      (HTMLElement & { dataProvider?: unknown }) | null
  )?.dataProvider;

describe('grid data providers survive a host re-render', () => {
  it('env-variables keeps its provider when edit mode toggles', async () => {
    const el = document.createElement('env-variables') as HTMLElement & {
      envLoaded: boolean;
      updateComplete: Promise<unknown>;
    };
    // The whole tab is behind `envLoaded`, which the environment call sets.
    el.envLoaded = true;
    document.body.appendChild(el);
    await el.updateComplete;
    await settle();

    const before = providerOf(el);
    expect(before, 'grid has a data provider').to.not.equal(undefined);

    // What pressing Edit does: the host records the row id, which is @state().
    (el as unknown as { _editingValueId: number })._editingValueId = 1;
    await el.updateComplete;
    await settle();

    expect(
      providerOf(el),
      'same provider, so Vaadin does not clear the cache and refetch'
    ).to.equal(before);

    el.remove();
  });

  it('page-variables-value-lookup keeps its provider too', async () => {
    // The sibling that already used a stable field — pinned so the two stay
    // consistent.
    const el = document.createElement(
      'page-variables-value-lookup'
    ) as HTMLElement & { updateComplete: Promise<unknown> };
    document.body.appendChild(el);
    await el.updateComplete;
    await settle();

    const before = providerOf(el);
    expect(before, 'grid has a data provider').to.not.equal(undefined);

    (el as unknown as { _editingValueId: number })._editingValueId = 1;
    await el.updateComplete;
    await settle();

    expect(providerOf(el), 'same provider').to.equal(before);

    el.remove();
  });
});
