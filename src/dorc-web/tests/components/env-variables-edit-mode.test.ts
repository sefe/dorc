import { expect } from '../_helpers';
import { vi } from 'vitest';
import { throwError } from 'rxjs';
import { PropertyValuesApi } from '../../src/apis/dorc-api';
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

  it('env-variables clears loadingScopeOptions and opens ErrorNotification when ScopeOptions fails', async () => {
    const collisionMessage =
      "Tags 'A B' and 'A_B' both produce deployment variable 'ServerNames_A_B'. Rename one tag before deployment.";
    const scopeSpy = vi
      .spyOn(PropertyValuesApi.prototype, 'propertyValuesScopeOptionsGet')
      .mockReturnValue(
        throwError(() => ({
          status: 400,
          response: collisionMessage
        })) as any
      );

    try {
      const el = document.createElement('env-variables') as HTMLElement & {
        envLoaded: boolean;
        environmentName: string;
        loadingScopeOptions: boolean;
        updateComplete: Promise<unknown>;
      };
      el.envLoaded = true;
      el.environmentName = 'Endur DV 10';
      document.body.appendChild(el);
      await el.updateComplete;
      await settle();

      (el as any).loadingScopeOptions = true;
      (el as any).getAllVariableNames();
      await el.updateComplete;

      expect(el.loadingScopeOptions, 'spinner flag cleared on error').to.equal(
        false
      );
      const notification = el.shadowRoot?.querySelector('error-notification');
      expect(notification, 'error notification mounted').to.not.equal(null);
      expect(notification?.getAttribute('errorMessage')).to.equal(
        collisionMessage
      );

      el.remove();
    } finally {
      scopeSpy.mockRestore();
    }
  });

  it('displays explicit Secure = true warning banner on env-variables when EnvironmentSecure is true', async () => {
    const el = document.createElement('env-variables') as HTMLElement & {
      envLoaded: boolean;
      environment: { EnvironmentName?: string; EnvironmentSecure?: boolean };
      updateComplete: Promise<unknown>;
    };
    el.envLoaded = true;
    el.environment = {
      EnvironmentName: 'STP PR 01',
      EnvironmentSecure: true
    };
    document.body.appendChild(el);
    await el.updateComplete;
    await settle();

    const dismissible = el.shadowRoot?.querySelector('dismissible-item') as
      | (HTMLElement & { message?: string })
      | null;
    expect(dismissible).to.not.equal(null);
    expect(dismissible?.message).to.contain(
      'This environment is secure (Secure = true): global (default) variable values are excluded during deployments'
    );

    el.environment = {
      EnvironmentName: 'STP UT 01',
      EnvironmentSecure: false
    };
    await el.updateComplete;
    const nextDismissible = el.shadowRoot?.querySelector('dismissible-item') as
      | (HTMLElement & { message?: string })
      | null;
    expect(nextDismissible?.message).to.contain('This environment is not secure');

    // Non-destructive built-in variable insertion + one-click lone $ escape on env-variables
    const valInput = el.shadowRoot?.querySelector(
      '#newVariableValue'
    ) as HTMLElement & { value: string };
    valInput.value = '\\\\\\E$\\Apps\\';
    const innerInput = valInput.querySelector('input');
    if (innerInput) {
      innerInput.value = '\\\\\\E$\\Apps\\';
      innerInput.setSelectionRange(2, 2);
    }
    (el as any).insertTokenIntoValue('$ServerNames_STP_Strategies$');
    expect(valInput.value).to.equal(
      '\\\\$ServerNames_STP_Strategies$\\E$\\Apps\\'
    );
    await el.updateComplete;
    const escapeBtn = el.shadowRoot?.querySelector(
      '#escapeDollarBtn'
    ) as HTMLButtonElement | null;
    expect(escapeBtn).to.not.equal(null);
    escapeBtn?.click();
    await el.updateComplete;
    expect(valInput.value).to.equal(
      '\\\\$ServerNames_STP_Strategies$\\E$$\\Apps\\'
    );

    // Inline $ IntelliSense + $x$ Explorer + Tokenised Segment Strip on env-variables
    (el as any).propertyValueScopeOptions = [
      {
        ValueOption: '$ServerNames_STP_Strategies$',
        SampleResolvedValue: 'stp-ut-srv01'
      },
      {
        ValueOption: '$LegacyTag1;LegacyTag2$',
        SampleResolvedValue: 'LegacyTag1;LegacyTag2'
      }
    ];
    await el.updateComplete;

    const strip = el.shadowRoot?.querySelector('#dollarTokenSegmentStrip');
    expect(strip).to.not.equal(null);
    expect(strip?.textContent).to.contain('Resolves (');
    expect(strip?.textContent).to.contain('stp-ut-srv01');
    const resolvedPill = strip?.querySelector('.dollar-token-pill--resolved');
    expect(resolvedPill).to.not.equal(null);

    const exploreBtn = el.shadowRoot?.querySelector(
      '#exploreDollarTokensBtn'
    ) as HTMLButtonElement | null;
    expect(exploreBtn).to.not.equal(null);
    exploreBtn?.click();
    await el.updateComplete;
    expect(
      el.shadowRoot?.querySelector('#dollarTokenIntellisensePopover')
    ).to.not.equal(null);

    valInput.value = '$Serv';
    if (innerInput) {
      innerInput.value = '$Serv';
      innerInput.setSelectionRange(5, 5);
    }
    valInput.dispatchEvent(new Event('input', { bubbles: true }));
    await el.updateComplete;

    const optionEl = el.shadowRoot?.querySelector(
      '.token-intellisense-item[data-token="$ServerNames_STP_Strategies$"]'
    ) as HTMLElement | null;
    expect(optionEl).to.not.equal(null);
    optionEl?.click();
    await el.updateComplete;
    expect(valInput.value).to.equal('$ServerNames_STP_Strategies$');

    el.remove();
  });
});
