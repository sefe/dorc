import { vi } from 'vitest';
import { of } from 'rxjs';
import { expect, fixture, html, settle } from '../_helpers';
import {
  extractReferencedPropertyNames,
  parsePropertyTokens
} from '../../src/variable-resolution/property-token-parser';
import {
  computeResolutionContextKey,
  PropertyResolutionClientCache
} from '../../src/variable-resolution/property-resolution-client-cache';
import {
  evaluateStringWithTraces,
  getTokenBadgeAppearance,
  HegsJsonViewer
} from '../../src/components/hegs-json-viewer';
import '../../src/components/hegs-json-viewer';
import type { PropertyResolutionTraceApiModel } from '../../src/apis/dorc-api';

const resolvePreviewPostMock = vi.hoisted(() => vi.fn());

vi.mock('../../src/apis/dorc-api', async importOriginal => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    PropertyValuesApi: class {
      apiPropertyValuesResolvePreviewPost = resolvePreviewPostMock;
    }
  };
});

describe('property-token-parser', () => {
  it('parses empty, static, dollar, brace, escaped-dollar, fn:, and unclosed tokens', () => {
    expect(parsePropertyTokens('')).to.deep.equal([]);

    expect(parsePropertyTokens('plain-text')).to.deep.equal([
      { kind: 'static', value: 'plain-text', rawText: 'plain-text' }
    ]);

    expect(parsePropertyTokens('${Env.Host_Name-1}')).to.deep.equal([
      {
        kind: 'property',
        propertyName: 'Env.Host_Name-1',
        rawText: '${Env.Host_Name-1}',
        syntax: 'brace'
      }
    ]);

    expect(
      parsePropertyTokens('fn:https://$Host$/$$price/$Port$tail')
    ).to.deep.equal([
      {
        kind: 'expression-prefix',
        rawText: 'fn:',
        expressionBody: 'https://$Host$/$$price/$Port$tail'
      },
      { kind: 'static', value: 'https://', rawText: 'https://' },
      {
        kind: 'property',
        propertyName: 'Host',
        rawText: '$Host$',
        syntax: 'dollar'
      },
      { kind: 'static', value: '/', rawText: '/' },
      { kind: 'escaped-dollar', value: '$', rawText: '$$' },
      { kind: 'static', value: 'price/', rawText: 'price/' },
      {
        kind: 'property',
        propertyName: 'Port',
        rawText: '$Port$',
        syntax: 'dollar'
      },
      { kind: 'static', value: 'tail', rawText: 'tail' }
    ]);

    expect(parsePropertyTokens('prefix$')).to.deep.equal([
      { kind: 'static', value: 'prefix', rawText: 'prefix' },
      { kind: 'unclosed-token', rawText: '$' }
    ]);

    expect(parsePropertyTokens('prefix$Unclosed')).to.deep.equal([
      { kind: 'static', value: 'prefix', rawText: 'prefix' },
      { kind: 'unclosed-token', rawText: '$Unclosed' }
    ]);
  });

  it('extracts unique referenced property names across objects, arrays, keys, and skips DOM nodes', () => {
    const domSpan = document.createElement('span');
    const data = {
      '$KeyProp$': 'https://$Host$:$Port$/$host$',
      EmptyToken: '$   $',
      BraceToken: '${TerraformVar}',
      NestedArray: ['$$literal', '$ArrayProp$', 42, null, domSpan]
    };

    expect(extractReferencedPropertyNames(data)).to.deep.equal([
      'KeyProp',
      'Host',
      'Port',
      'TerraformVar',
      'ArrayProp'
    ]);
  });
});

describe('PropertyResolutionClientCache', () => {
  beforeEach(() => {
    resolvePreviewPostMock.mockReset();
  });

  it('computes normalized context keys with sensitive overrides redacted', () => {
    const key1 = computeResolutionContextKey({});
    expect(key1).to.equal('{"env":"","proj":"","overrides":[]}');

    const key2 = computeResolutionContextKey({
      environmentName: '  Endur-UT1 ',
      projectName: ' AppProj ',
      requestOverrides: [
        { PropertyName: '   ', PropertyValue: 'ignored' },
        { PropertyName: 'ZProp', PropertyValue: 'zval', IsSensitive: false },
        { PropertyName: 'AProp', PropertyValue: 'secret', IsSensitive: true },
        { PropertyName: 'MProp', PropertyValue: undefined }
      ]
    });
    expect(key2).to.equal(
      JSON.stringify({
        env: 'endur-ut1',
        proj: 'appproj',
        overrides: [
          { name: 'aprop', val: '[REDACTED]', sec: true },
          { name: 'mprop', val: '', sec: false },
          { name: 'zprop', val: 'zval', sec: false }
        ]
      })
    );
  });

  it('uses default transport, coalesces concurrent calls, serves from cache, and handles errors', async () => {
    resolvePreviewPostMock.mockReturnValueOnce(
      of([
        {
          PropertyName: 'Host',
          Source: 'EnvironmentScope',
          ScopeName: 'UT1',
          ResolvedValueJson: '"ut1.internal"'
        },
        {
          PropertyName: undefined
        }
      ])
    );

    const defaultCache = new PropertyResolutionClientCache({
      coalesceWindowMs: 5,
      ttlMs: 10_000
    });

    const emptyMap = await defaultCache.resolveProperties({}, ['   ']);
    expect(emptyMap.size).to.equal(0);

    const [firstBatch, secondBatch] = await Promise.all([
      defaultCache.resolveProperties(
        { environmentName: 'UT1', projectName: 'Proj' },
        ['Host', 'host']
      ),
      defaultCache.resolveProperties(
        { environmentName: 'UT1', projectName: 'Proj' },
        ['Host', 'MissingProp']
      )
    ]);

    expect(resolvePreviewPostMock.mock.calls).to.have.length(1);
    expect(firstBatch.get('host')?.ResolvedValueJson).to.equal('"ut1.internal"');
    expect(secondBatch.has('missingprop')).to.equal(false);

    // Hit warm cache for 'Host' without transport call
    const cachedHit = await defaultCache.resolveProperties(
      { environmentName: 'UT1', projectName: 'Proj' },
      ['Host']
    );
    expect(resolvePreviewPostMock.mock.calls).to.have.length(1);
    expect(cachedHit.get('host')?.ScopeName).to.equal('UT1');

    // Clear cache and test transport rejection
    defaultCache.clear();
    const failingCache = new PropertyResolutionClientCache({
      coalesceWindowMs: 1,
      transport: async () => {
        throw new Error('Backend error');
      }
    });

    let caughtError: Error | undefined;
    try {
      await failingCache.resolveProperties({}, ['FailProp']);
    } catch (err) {
      caughtError = err as Error;
    }
    expect(caughtError?.message).to.equal('Backend error');
  });
});

describe('hegs-json-viewer contextual property resolution', () => {
  it('evaluates string traces and badge appearances across all sources and diagnostics', () => {
    const traces = new Map<string, PropertyResolutionTraceApiModel>([
      [
        'host',
        {
          PropertyName: 'Host',
          Source: 'EnvironmentScope',
          ScopeName: 'UT1',
          ResolvedValueJson: '"ut1.local"',
          Diagnostic: 'Resolved'
        }
      ],
      [
        'port',
        {
          PropertyName: 'Port',
          Source: 'GlobalDefault',
          ResolvedValueJson: '8443',
          Diagnostic: 'Resolved'
        }
      ],
      [
        'unresolved',
        {
          PropertyName: 'Unresolved',
          Source: 'Unresolved',
          Diagnostic: 'NotFound'
        }
      ]
    ]);

    expect(evaluateStringWithTraces('literal$$text', traces)).to.deep.equal({
      status: 'literal',
      resolvedValue: 'literal$text',
      resolvedDisplayString: 'literal$text'
    });

    expect(evaluateStringWithTraces('$Port$', traces)).to.deep.equal({
      status: 'resolved',
      resolvedValue: 8443,
      resolvedDisplayString: '8443'
    });

    expect(evaluateStringWithTraces('$Host$', traces)).to.deep.equal({
      status: 'resolved',
      resolvedValue: 'ut1.local',
      resolvedDisplayString: 'ut1.local'
    });

    expect(
      evaluateStringWithTraces('https://$Host$:$Port$/$$cost', traces)
    ).to.deep.equal({
      status: 'resolved',
      resolvedValue: 'https://ut1.local:8443/$cost',
      resolvedDisplayString: 'https://ut1.local:8443/$cost'
    });

    expect(
      evaluateStringWithTraces('https://$Unresolved$/path', traces).status
    ).to.equal('unresolved');

    expect(getTokenBadgeAppearance(undefined).badgeLabel).to.equal(
      'Resolving...'
    );
    expect(
      getTokenBadgeAppearance({ Diagnostic: 'ExpressionDeferred' }).badgeLabel
    ).to.equal('fn: Deferred to Deploy');
    expect(
      getTokenBadgeAppearance({ Diagnostic: 'ExpressionBlockedByOverride' })
        .badgeLabel
    ).to.equal('fn: Blocked by Override');
    expect(
      getTokenBadgeAppearance({ Diagnostic: 'CircularReference' }).badgeLabel
    ).to.equal('Circular Reference');
    expect(
      getTokenBadgeAppearance({ IsSecure: true, ScopeName: 'Prod' }).badgeLabel
    ).to.equal('Secret (Prod)');
    expect(
      getTokenBadgeAppearance({ IsSecure: true, ScopeName: null }).badgeLabel
    ).to.equal('Secret (Redacted)');
    expect(
      getTokenBadgeAppearance({ Source: 'RequestOverride' }).badgeLabel
    ).to.equal('Request Override');
    expect(
      getTokenBadgeAppearance({ Source: 'EnvironmentScope', ScopeName: 'UT1' })
        .badgeLabel
    ).to.equal('Env (UT1)');
    expect(
      getTokenBadgeAppearance({ Source: 'EnvironmentScope', ScopeName: null })
        .badgeLabel
    ).to.equal('Env (Current)');
    expect(
      getTokenBadgeAppearance({
        Source: 'AncestorEnvironmentScope',
        ScopeName: 'ParentEnv'
      }).badgeLabel
    ).to.equal('Inherited (ParentEnv)');
    expect(
      getTokenBadgeAppearance({
        Source: 'AncestorEnvironmentScope',
        ScopeName: null
      }).badgeLabel
    ).to.equal('Inherited (Parent)');
    expect(
      getTokenBadgeAppearance({ Source: 'GlobalDefault' }).badgeLabel
    ).to.equal('Default (Global)');
    expect(
      getTokenBadgeAppearance({
        Source: 'BuiltInScopeOption',
        ValueType: 'String'
      }).badgeLabel
    ).to.equal('Built-in (String)');
    expect(
      getTokenBadgeAppearance({
        Source: 'BuiltInScopeOption',
        ValueType: null
      }).badgeLabel
    ).to.equal('Built-in (Scope)');
    expect(
      getTokenBadgeAppearance({ Source: 'RuntimeOnly' }).badgeLabel
    ).to.equal('Monitor Runtime');
    expect(
      getTokenBadgeAppearance({ Source: 'Unresolved' }).badgeLabel
    ).to.equal('Missing in Scope');
  });

  it('renders toolbar, mode switches (inline, raw, resolved), tokens, and inspector card', async () => {
    const customCache = new PropertyResolutionClientCache({
      coalesceWindowMs: 1,
      transport: async () => [
        {
          PropertyName: 'ApiHost',
          RawValue: '$BaseHost$.corp',
          ResolvedValueJson: '"api.ut1.corp"',
          ValueType: 'String',
          Source: 'EnvironmentScope',
          ScopeName: 'Endur-UT1',
          ResolutionChain: ['BaseHost', 'ApiHost'],
          IsSecure: false,
          Diagnostic: 'Resolved'
        },
        {
          PropertyName: 'DbPassword',
          RawValue: '[REDACTED]',
          ResolvedValueJson: '"[REDACTED]"',
          ValueType: 'String',
          Source: 'EnvironmentScope',
          ScopeName: 'Endur-UT1',
          ResolutionChain: ['DbPassword'],
          IsSecure: true,
          Diagnostic: 'SecretRedacted'
        },
        {
          PropertyName: 'MissingVar',
          RawValue: null,
          ResolvedValueJson: null,
          ValueType: 'Unresolved',
          Source: 'Unresolved',
          ScopeName: null,
          ResolutionChain: [],
          IsSecure: false,
          Diagnostic: 'NotFound'
        }
      ]
    });

    const el = await fixture<HegsJsonViewer>(
      html`<hegs-json-viewer
        .resolutionCache=${customCache}
        .resolutionContext=${{
          environmentName: 'Endur-UT1',
          projectName: 'Endur'
        }}
        .data=${{
          Endpoint: 'https://$ApiHost$/v1',
          Secret: '$DbPassword$',
          Missing: '$MissingVar$',
          Expr: 'fn:System.DateTime.UtcNow',
          Escaped: '$$100',
          Unclosed: '$Broken',
          StaticText: 'hello',
          Count: 3
        }}
      ></hegs-json-viewer>`
    );
    el.expandAll();
    await settle();
    await new Promise(r => setTimeout(r, 20));
    await settle();

    const root = el.shadowRoot!;
    expect(
      root.querySelector('[part="context-env"]')?.textContent?.trim()
    ).to.equal('Env: Endur-UT1');
    expect(
      root
        .querySelector('[part="context-counts"]')
        ?.textContent?.replace(/\s+/g, ' ')
        .trim()
    ).to.contain('3 properties (2 resolved, 1 missing, 1 secret)');

    // Inline mode shows resolved preview arrow
    expect(
      root.querySelector('[part="resolved-preview"]')?.textContent
    ).to.contain('https://api.ut1.corp/v1');

    // Click token to open inspector card
    const apiHostChip = root.querySelector<HTMLElement>(
      '[data-property-name="ApiHost"]'
    )!;
    apiHostChip.click();
    await settle();

    const inspector = root.querySelector('[part="inspector-card"]');
    expect(inspector).to.not.equal(null);
    expect(inspector?.textContent).to.contain('$BaseHost$.corp');
    expect(inspector?.querySelectorAll('.inspector-chain li')).to.have.length(
      2
    );
    expect(
      inspector?.querySelector<HTMLAnchorElement>('a.inspector-link')?.href
    ).to.contain('/variables/value-lookup?name=ApiHost&scope=Endur-UT1');

    // Click MissingVar token to inspect a trace with empty ResolutionChain and null ScopeName
    const missingChip = root.querySelector<HTMLElement>(
      '[data-property-name="MissingVar"]'
    )!;
    missingChip.click();
    await settle();
    expect(
      root.querySelector('[part="inspector-card"]')?.textContent
    ).to.contain('(not defined)');

    // Close inspector via close button
    root
      .querySelector<HTMLButtonElement>('[data-action="close-inspector"]')
      ?.click();
    await settle();
    expect(root.querySelector('[part="inspector-card"]')).to.equal(null);

    // Switch to Raw mode and Resolved mode via toolbar buttons
    root.querySelector<HTMLButtonElement>('[data-mode="raw"]')?.click();
    await settle();
    expect(el.displayMode).to.equal('raw');
    expect(root.querySelector('[part="resolved-preview"]')).to.equal(null);

    root.querySelector<HTMLButtonElement>('[data-mode="resolved"]')?.click();
    await settle();
    expect(el.displayMode).to.equal('resolved');
    expect(root.textContent).to.contain('"https://api.ut1.corp/v1"');

    // Global default scope label when environmentName is omitted, and singular '1 property'
    el.resolutionContext = {};
    el.data = { OnlyOne: '$ApiHost$' };
    await settle();
    await new Promise(r => setTimeout(r, 20));
    await settle();
    expect(
      root.querySelector('[part="context-env"]')?.textContent?.trim()
    ).to.equal('Scope: Global Default');
    expect(
      root
        .querySelector('[part="context-counts"]')
        ?.textContent?.replace(/\s+/g, ' ')
        .trim()
    ).to.contain('1 property');

    // Clearing resolutionContext resets traces
    el.resolutionContext = undefined;
    await settle();

    // Error in resolutionCache resets traces cleanly
    el.resolutionCache = new PropertyResolutionClientCache({
      coalesceWindowMs: 1,
      transport: async () => {
        throw new Error('Network failure');
      }
    });
    el.resolutionContext = { environmentName: 'Endur-UT1' };
    await settle();
    await new Promise(r => setTimeout(r, 20));
    await settle();
  });

  it('wires resolutionContext in call sites and query params in page-variables-value-lookup', async () => {
    await import('../../src/pages/page-variables-value-lookup');
    await import('../../src/pages/page-scripts-list');
    await import('../../src/pages/page-project-bundles');
    await import('../../src/components/deployment-definition-editor');
    await import('../../src/components/deploy/deploy-env');

    // 1. page-variables-value-lookup query param initialization
    const originalUrl = window.location.href;
    window.history.replaceState(
      {},
      '',
      '/variables/value-lookup?name=ApiHost&scope=Endur-UT1'
    );
    const lookupEl = document.createElement(
      'page-variables-value-lookup'
    ) as HTMLElement & {
      connectedCallback(): void;
      nameFilterValue?: string;
      scopeFilterValue?: string;
    };
    lookupEl.connectedCallback();
    expect(lookupEl.nameFilterValue).to.equal('ApiHost');
    expect(lookupEl.scopeFilterValue).to.equal('Endur-UT1');

    window.history.replaceState({}, '', '/variables/value-lookup');
    const lookupElEmpty = document.createElement(
      'page-variables-value-lookup'
    ) as HTMLElement & {
      connectedCallback(): void;
      nameFilterValue?: string;
      scopeFilterValue?: string;
    };
    lookupElEmpty.connectedCallback();
    expect(lookupElEmpty.nameFilterValue).to.equal('');
    expect(lookupElEmpty.scopeFilterValue).to.equal('');
    window.history.replaceState({}, '', originalUrl);

    // 2. deployment-definition-editor JSON path preview + environment selector
    const defEditor = await fixture<
      HTMLElement & {
        component: unknown;
        project: unknown;
      }
    >(
      html`<deployment-definition-editor
        .project=${{ ProjectName: 'Payments' }}
        .component=${{
          ComponentName: 'DeployStep',
          ComponentType: 'PowerShell',
          ScriptPath: '{"ScriptPath":"run.ps1","GenericArguments":[{"Name":"Env","Value":"$TargetEnv$"}]}'
        }}
      ></deployment-definition-editor>`
    );
    await settle();
    const envField = defEditor.shadowRoot!.querySelector<HTMLElement>(
      'vaadin-text-field[label="Preview as environment (optional)"]'
    )!;
    envField.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: 'Payments-UAT' } })
    );
    await settle();
    const defViewer = defEditor.shadowRoot!.querySelector<HegsJsonViewer>(
      'hegs-json-viewer.json-preview'
    )!;
    expect(defViewer.resolutionContext).to.deep.equal({
      environmentName: 'Payments-UAT',
      projectName: 'Payments',
      locationKind: 'component-definition'
    });
  });
});
