import { describe, it, expect, vi, beforeEach } from 'vitest';

// The Secure checkbox on the variables page has no `.checked` binding — the
// tick is whatever the DOM holds — so cancelling the confirmation has to put it
// back by hand. `updatePropertySecure` became async when window.confirm() was
// replaced, and two things stopped working at once: preventDefault() after an
// await is a no-op, and `event.target` is the light-DOM <input> inside
// vaadin-checkbox, not the host. Writing `.checked` to the input leaves the
// visible tick alone, so cancelling told an admin a property's values were
// encrypted when they were still plaintext.
//
// variables-secure-toggle.test.ts pins that mechanism on a bare checkbox. This
// drives the shipped handler on the shipped page: reverting `currentTarget` to
// `target` in page-variables.ts fails both tests here.

const { confirmCalls, confirmAnswer } = vi.hoisted(() => ({
  confirmCalls: [] as string[],
  confirmAnswer: { value: false }
}));

vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: (message: string) => {
    confirmCalls.push(message);
    return Promise.resolve(confirmAnswer.value);
  }
}));

await import('../../src/pages/page-variables');

type Page = HTMLElement & {
  properties?: { Name?: string; Secure?: boolean }[];
  isAdmin: boolean;
  existingPropertySelected: boolean;
};

const settle = async () => {
  for (let i = 0; i < 4; i++) {
    await new Promise(r => requestAnimationFrame(() => r(null)));
  }
};

const mount = async (secure: boolean) => {
  const page = document.createElement('page-variables') as Page;
  page.isAdmin = true;
  document.body.appendChild(page);
  await settle();
  // After the first render, not before: the property combo fires `value-changed`
  // with an empty value on its way up, which clears `propertyName` and
  // `existingPropertySelected` again — leaving the checkbox disabled and the
  // handler's `existingProperty` lookup empty, so nothing would be asked.
  page.existingPropertySelected = true;
  page.properties = [{ Name: 'Var1', Secure: secure }];
  (page as unknown as { propertyName: string }).propertyName = 'Var1';
  await settle();
  const checkbox = page.shadowRoot?.querySelector(
    '#is-variable-secure'
  ) as HTMLElement & { checked: boolean };
  // The checkbox carries no `.checked` binding, so seed it to match the model
  // the way the page's own load path leaves it.
  checkbox.checked = secure;
  await settle();
  return { page, checkbox };
};

describe('page-variables Secure toggle', () => {
  beforeEach(() => {
    confirmCalls.length = 0;
    confirmAnswer.value = false;
  });

  it('clears the tick again when securing is cancelled', async () => {
    const { page, checkbox } = await mount(false);

    (checkbox.querySelector('input') as HTMLInputElement).click();
    await settle();

    // The click has to have reached the handler, or the assertions below hold
    // for the wrong reason.
    expect(confirmCalls.length, 'the handler asked').toBe(1);
    expect(confirmCalls[0]).toContain('mark property "Var1" as secure');

    expect(checkbox.checked, 'host tick reverted on cancel').toBe(false);
    expect(checkbox.hasAttribute('checked'), 'host reflects it').toBe(false);
    expect(page.properties?.[0].Secure, 'model untouched').toBe(false);
    page.remove();
  });

  it('restores the tick again when unsecuring is cancelled', async () => {
    // The mirror image: cancelling must not leave a secured property looking
    // plaintext.
    const { page, checkbox } = await mount(true);

    (checkbox.querySelector('input') as HTMLInputElement).click();
    await settle();

    expect(confirmCalls.length, 'the handler asked').toBe(1);
    expect(confirmCalls[0]).toContain('mark property "Var1" as non-secure');

    expect(checkbox.checked, 'host tick restored on cancel').toBe(true);
    expect(checkbox.hasAttribute('checked'), 'host reflects it').toBe(true);
    expect(page.properties?.[0].Secure, 'model untouched').toBe(true);
    page.remove();
  });

  it('clears loadingScopeOptions and opens ErrorNotification when ScopeOptions fails', async () => {
    const { throwError } = await import('rxjs');
    const { PropertyValuesApi } = await import('../../src/apis/dorc-api');
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
      const { page } = await mount(false);
      (page as any)._newVariableValueScopeChanged({
        target: { value: 'Endur DV 10' }
      } as unknown as CustomEvent);

      expect(
        (page as any).loadingScopeOptions,
        'spinner flag cleared on error'
      ).toBe(false);
      const notification = page.shadowRoot?.querySelector('error-notification');
      expect(notification).not.toBeNull();
      expect(notification?.getAttribute('errorMessage')).toBe(collisionMessage);
      page.remove();
    } finally {
      scopeSpy.mockRestore();
    }
  });

  it('loads ScopeOptions for (default) scope, inserts tokens into Value without overwriting, and submits multi-scope values', async () => {
    const { of } = await import('rxjs');
    const { PropertyValuesApi } = await import('../../src/apis/dorc-api');
    const scopeCalls: string[] = [];
    const scopeSpy = vi
      .spyOn(PropertyValuesApi.prototype, 'propertyValuesScopeOptionsGet')
      .mockImplementation((req?: { propertyValueScope?: string }) => {
        scopeCalls.push(req?.propertyValueScope ?? '');
        return of([
          {
            ValueOption: '$ServerNames_STP_Strategies$',
            SampleResolvedValue: 'stp-srv-01'
          }
        ]) as any;
      });
    const postPayloads: any[] = [];
    const postSpy = vi
      .spyOn(PropertyValuesApi.prototype, 'propertyValuesPost')
      .mockImplementation((req: any) => {
        postPayloads.push(req);
        return of([{ Status: 'success', Item: {} }]) as any;
      });

    try {
      const { page } = await mount(false);
      // Load default scope options when scope is empty
      (page as any).loadScopeOptions('');
      await settle();
      expect(scopeCalls).toContain('');
      expect((page as any).propertyValueScopeOptions.length).toBe(1);

      // Insert resolver token into existing path text without overwriting
      const valInput = page.shadowRoot?.querySelector(
        '#newVariableValue'
      ) as HTMLElement & { value: string };
      valInput.value = '\\\\\\E$\\Apps\\';
      const innerInput = valInput.querySelector('input');
      if (innerInput) {
        innerInput.value = '\\\\\\E$\\Apps\\';
        innerInput.setSelectionRange(2, 2);
      }
      (page as any).insertTokenIntoValue('$ServerNames_STP_Strategies$');
      expect(valInput.value).toBe(
        '\\\\$ServerNames_STP_Strategies$\\E$\\Apps\\'
      );

      // One-click escape of lone $ (E$ -> E$$) while keeping $ServerNames_STP_Strategies$ intact
      await settle();
      const escapeBtn = page.shadowRoot?.querySelector(
        '#escapeDollarBtn'
      ) as HTMLButtonElement | null;
      expect(escapeBtn).not.toBeNull();
      escapeBtn?.click();
      await settle();
      expect(valInput.value).toBe(
        '\\\\$ServerNames_STP_Strategies$\\E$$\\Apps\\'
      );

      // Multi-select Default + STP UT 01 + STP PR 01 and submit batch
      (page as any).toggleDefaultScopeSelection();
      const envScopeCombo = page.shadowRoot?.querySelector(
        '#envScope'
      ) as HTMLElement & { value: string };
      envScopeCombo.value = 'STP UT 01';
      (page as any).queueCurrentEnvironmentScope();
      envScopeCombo.value = 'STP PR 01';
      (page as any).queueCurrentEnvironmentScope();
      await settle();

      expect((page as any).selectedVariableScopes).toEqual([
        '',
        'STP UT 01',
        'STP PR 01'
      ]);

      (page as any)._addVariableValueClick();
      await settle();

      expect(postPayloads.length).toBe(1);
      expect(
        postPayloads[0].propertyValueDto.map(
          (d: any) => d.PropertyValueFilter ?? ''
        )
      ).toEqual(['', 'STP UT 01', 'STP PR 01']);
      page.remove();
    } finally {
      scopeSpy.mockRestore();
      postSpy.mockRestore();
    }
  });

  it('opens inline $ IntelliSense on typing $Serv, supports $x$ explorer, excludes semicolon tags, and renders live tokenised segment pills', async () => {
    const { of } = await import('rxjs');
    const { PropertyValuesApi } = await import('../../src/apis/dorc-api');
    const { segmentizeDollarValueWithTraces } = await import(
      '../../src/variable-resolution/resolution-presentation'
    );

    const scopeSpy = vi
      .spyOn(PropertyValuesApi.prototype, 'propertyValuesScopeOptionsGet')
      .mockReturnValue(
        of([
          {
            ValueOption: '$ServerNames_STP_Strategies$',
            SampleResolvedValue: 'GMDV-ENDAPP30'
          },
          {
            ValueOption: '$ABAgentV12;appserv02$',
            SampleResolvedValue: 'ABAgentV12;appserv02'
          }
        ]) as any
      );

    try {
      const { page } = await mount(false);
      (page as any).newVariableScope = 'Endur DV 10';
      (page as any).loadScopeOptions('Endur DV 10');
      await settle();

      const filteredTokens = (page as any).getFilteredCompletionTokens();
      expect(
        filteredTokens.some(
          (t: { ValueOption: string }) =>
            t.ValueOption === '$ABAgentV12;appserv02$'
        ),
        'semicolon-joined legacy tag strings are excluded'
      ).toBe(false);

      // Clicking the $x$ suffix explorer button opens the token explorer for new users
      const exploreBtn = page.shadowRoot?.querySelector(
        '#exploreDollarTokensBtn'
      ) as HTMLButtonElement | null;
      expect(exploreBtn).not.toBeNull();
      exploreBtn?.click();
      await settle();

      let popover = page.shadowRoot?.querySelector(
        '#dollarTokenIntellisensePopover'
      );
      expect(popover).not.toBeNull();
      expect(
        popover?.querySelector('#tokenExplorerSearchInput')
      ).not.toBeNull();

      // Typing $Serv in #newVariableValue triggers inline IntelliSense and Tab replaces $Serv at caret
      const valInput = page.shadowRoot?.querySelector(
        '#newVariableValue'
      ) as HTMLElement & { value: string };
      const innerInput = valInput.querySelector('input');
      valInput.value = '\\\\$Serv';
      if (innerInput) {
        innerInput.value = '\\\\$Serv';
        innerInput.setSelectionRange(7, 7);
      }
      valInput.dispatchEvent(new Event('input', { bubbles: true }));
      await settle();

      popover = page.shadowRoot?.querySelector(
        '#dollarTokenIntellisensePopover'
      );
      expect(popover).not.toBeNull();
      expect((page as any).inlineDollarQuery).toBe('Serv');

      valInput.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Tab', bubbles: true })
      );
      await settle();

      expect(valInput.value).toBe('\\\\$ServerNames_STP_Strategies$');
      expect(
        page.shadowRoot?.querySelector('#dollarTokenIntellisensePopover')
      ).toBeNull();

      // Verify live tokenised segment strip and per-token provenance pills
      const strip = page.shadowRoot?.querySelector('#dollarTokenSegmentStrip');
      expect(strip).not.toBeNull();
      expect(strip?.textContent).toContain('Resolves (Endur DV 10):');
      expect(strip?.textContent).toContain('\\\\GMDV-ENDAPP30');

      const pill = strip?.querySelector(
        '.dollar-token-pill--resolved'
      ) as HTMLElement | null;
      expect(pill).not.toBeNull();
      expect(pill?.getAttribute('title')).toContain(
        'Env (Endur DV 10) -> GMDV-ENDAPP30'
      );

      // Direct unit check of segmentizeDollarValueWithTraces across all segment kinds & statuses
      const traces = new Map<string, any>([
        [
          'ServerNames_Web',
          {
            PropertyName: 'ServerNames_Web',
            Source: 'EnvironmentScope',
            ScopeName: 'Endur DV 10',
            ResolvedValueJson: '"GMDV-ENDAPP30"',
            Diagnostic: 'None'
          }
        ],
        [
          'DefaultRoot',
          {
            PropertyName: 'DefaultRoot',
            Source: 'GlobalDefault',
            ScopeName: 'Default',
            ResolvedValueJson: '"D:\\\\Apps"',
            Diagnostic: 'FallbackToDefault'
          }
        ],
        [
          'DbPassword',
          {
            PropertyName: 'DbPassword',
            Source: 'EnvironmentScope',
            ScopeName: 'Endur DV 10',
            IsSecure: true,
            Diagnostic: 'Masked'
          }
        ]
      ]);

      const segments = segmentizeDollarValueWithTraces(
        '\\\\$ServerNames_Web$\\E$$\\$DefaultRoot$\\$MissingVar$\\$DbPassword$',
        traces
      );
      const propSegments = segments.filter(s => s.kind === 'property');
      expect(propSegments.map(s => s.status)).toEqual([
        'resolved',
        'fallback',
        'unresolved',
        'masked'
      ]);
      expect(propSegments[0].tooltip).toBe(
        'Env (Endur DV 10) -> GMDV-ENDAPP30'
      );
      expect(propSegments[1].tooltip).toBe('Default fallback -> D:\\Apps');
      expect(propSegments[2].tooltip).toBe('Missing in scope');
      expect(propSegments[3].tooltip).toContain('Secret');
      expect(
        segments.some(
          s =>
            s.kind === 'escaped-dollar' &&
            s.tooltip === 'Escaped literal $ ($$)'
        )
      ).toBe(true);

      // Verify typing fn: triggers fn: expression IntelliSense and $ inside fn: triggers $Variable$ completion
      valInput.value = 'fn:';
      if (innerInput) {
        innerInput.value = 'fn:';
        innerInput.setSelectionRange(3, 3);
      }
      valInput.dispatchEvent(new Event('input', { bubbles: true }));
      await settle();

      popover = page.shadowRoot?.querySelector(
        '#dollarTokenIntellisensePopover'
      );
      expect(popover).not.toBeNull();
      expect((page as any).inlineDollarQuery).toBe('fn:');
      expect(
        (page as any)
          .getFilteredCompletionTokens()
          .every((t: { ValueOption: string }) =>
            t.ValueOption.startsWith('fn:')
          )
      ).toBe(true);

      // Typing $Serv inside an fn: expression triggers $Variable$ completion and renders fn: + token pills
      valInput.value = 'fn:System.IO.Path.Combine("$Serv", "app")';
      if (innerInput) {
        innerInput.value = valInput.value;
        innerInput.setSelectionRange(32, 32);
      }
      valInput.dispatchEvent(new Event('input', { bubbles: true }));
      await settle();

      expect((page as any).inlineDollarQuery).toBe('Serv');
      valInput.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Tab', bubbles: true })
      );
      await settle();

      expect(valInput.value).toBe(
        'fn:System.IO.Path.Combine("$ServerNames_STP_Strategies$", "app")'
      );

      const fnStrip = page.shadowRoot?.querySelector(
        '#dollarTokenSegmentStrip'
      );
      expect(fnStrip).not.toBeNull();
      expect(
        fnStrip?.querySelector('[data-token-status="expression-prefix"]')
          ?.textContent
      ).toBe('fn:');
      expect(fnStrip?.textContent).toContain(
        'fn:System.IO.Path.Combine("GMDV-ENDAPP30", "app")'
      );

      page.remove();
    } finally {
      scopeSpy.mockRestore();
    }
  });
});
