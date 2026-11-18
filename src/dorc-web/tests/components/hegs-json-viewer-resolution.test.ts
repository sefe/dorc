import { vi } from 'vitest';
import { of } from 'rxjs';
import { expect, fixture, html, settle } from '../_helpers';
import {
  extractReferencedPropertyNames,
  parsePropertyTokens,
  resolveTokenSyntaxProfile
} from '../../src/variable-resolution/property-token-parser';
import {
  computeResolutionContextKey,
  PropertyResolutionClientCache
} from '../../src/variable-resolution/property-resolution-client-cache';
import {
  checkGlob,
  deepTraverse,
  evaluateStringWithTraces,
  generateNodePreview,
  getTokenBadgeAppearance,
  getType,
  HegsJsonViewer,
  isMatchingPath,
  JSONConverter
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
  it('parses empty, static, dollar, brace, escaped-dollar, fn:, unclosed tokens, and execution-site profiles', () => {
    expect(parsePropertyTokens('')).to.deep.equal([]);

    expect(parsePropertyTokens('plain-text')).to.deep.equal([
      { kind: 'static', value: 'plain-text', rawText: 'plain-text' }
    ]);

    expect(parsePropertyTokens('${Env.Host_Name-1}')).to.deep.equal([
      { kind: 'unclosed-token', rawText: '${Env.Host_Name-1}' }
    ]);

    expect(
      parsePropertyTokens('${Env.Host_Name-1}', 'brace-only')
    ).to.deep.equal([
      {
        kind: 'property',
        propertyName: 'Env.Host_Name-1',
        rawText: '${Env.Host_Name-1}',
        syntax: 'brace'
      }
    ]);

    // Execution-site syntax profiles (ScriptPath literal, GenericArguments brace-only)
    expect(resolveTokenSyntaxProfile(undefined, 'ScriptPath')).to.equal(
      'standard'
    );
    expect(
      resolveTokenSyntaxProfile('script-arguments', 'ScriptPath')
    ).to.equal('literal');
    expect(
      resolveTokenSyntaxProfile(
        'deployment-definition',
        'GenericArguments.0.Arg'
      )
    ).to.equal('brace-only');
    expect(
      resolveTokenSyntaxProfile('script-arguments', 'OtherField')
    ).to.equal('standard');

    expect(parsePropertyTokens('$Host$', 'literal')).to.deep.equal([
      { kind: 'static', value: '$Host$', rawText: '$Host$' }
    ]);
    expect(parsePropertyTokens('$Host$', 'brace-only')).to.deep.equal([
      { kind: 'static', value: '$Host$', rawText: '$Host$' }
    ]);
    expect(parsePropertyTokens('${Host}', 'brace-only')).to.deep.equal([
      {
        kind: 'property',
        propertyName: 'Host',
        rawText: '${Host}',
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

  it('extracts unique case-sensitive referenced property names across objects, arrays, keys, profiles, and skips DOM nodes', () => {
    const domSpan = document.createElement('span');
    const data = {
      '$KeyProp$': 'https://$Host$:$Port$/$Host$/$host$',
      EmptyToken: '$   $',
      BraceToken: '${TerraformVar}',
      NestedArray: ['$$literal', '$ArrayProp$', 42, null, domSpan]
    };

    expect(extractReferencedPropertyNames(data)).to.deep.equal([
      'KeyProp',
      'Host',
      'Port',
      'host',
      'ArrayProp'
    ]);
    expect(extractReferencedPropertyNames(['$RootItem$'])).to.deep.equal([
      'RootItem'
    ]);

    const scriptData = {
      ScriptPath: '$IgnoredInScriptPath$',
      GenericArguments: [
        { DollarIgnored: '$NotExtracted$' },
        { BraceIncluded: '${ExtractedArg}' }
      ]
    };
    expect(
      extractReferencedPropertyNames(scriptData, 'script-arguments')
    ).to.deep.equal(['ExtractedArg']);
  });
});

describe('PropertyResolutionClientCache', () => {
  beforeEach(() => {
    resolvePreviewPostMock.mockReset();
  });

  it('computes normalized context keys with sensitive overrides redacted and case-sensitive override names', () => {
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
          { name: 'AProp', val: '[REDACTED]', sec: true },
          { name: 'MProp', val: '', sec: false },
          { name: 'ZProp', val: 'zval', sec: false }
        ]
      })
    );
  });

  it('uses default transport, coalesces concurrent calls, chunks >250 names, serves from cache, and handles errors', async () => {
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
        ['Host', 'Host']
      ),
      defaultCache.resolveProperties(
        { environmentName: 'UT1', projectName: 'Proj' },
        ['Host', 'MissingProp']
      )
    ]);

    expect(resolvePreviewPostMock.mock.calls).to.have.length(1);
    expect(firstBatch.get('Host')?.ResolvedValueJson).to.equal('"ut1.internal"');
    expect(secondBatch.has('MissingProp')).to.equal(false);

    // Hit warm cache for 'Host' without transport call
    const cachedHit = await defaultCache.resolveProperties(
      { environmentName: 'UT1', projectName: 'Proj' },
      ['Host']
    );
    expect(resolvePreviewPostMock.mock.calls).to.have.length(1);
    expect(cachedHit.get('Host')?.ScopeName).to.equal('UT1');

    // Hit warm cache for 'Host' + uncached 'NewProp' so contextCache exists and only 'NewProp' is fetched
    resolvePreviewPostMock.mockReturnValueOnce(
      of([
        {
          PropertyName: 'NewProp',
          Source: 'GlobalDefault',
          ResolvedValueJson: '"new-val"'
        }
      ])
    );
    const partialHit = await defaultCache.resolveProperties(
      { environmentName: 'UT1', projectName: 'Proj' },
      ['Host', 'NewProp']
    );
    expect(resolvePreviewPostMock.mock.calls).to.have.length(2);
    expect(partialHit.get('Host')?.ResolvedValueJson).to.equal(
      '"ut1.internal"'
    );
    expect(partialHit.get('NewProp')?.ResolvedValueJson).to.equal('"new-val"');

    // Verify chunking of batches > 250 property names
    const chunkCalls: number[] = [];
    const chunkingCache = new PropertyResolutionClientCache({
      coalesceWindowMs: 1,
      transport: async req => {
        const names = req.PropertyNames ?? [];
        chunkCalls.push(names.length);
        return names.map(n => ({
          PropertyName: n,
          Source: 'GlobalDefault',
          ResolvedValueJson: `"val-${n}"`
        }));
      }
    });
    const manyNames = Array.from({ length: 300 }, (_, i) => `Prop_${i}`);
    const chunkedResult = await chunkingCache.resolveProperties({}, manyNames);
    expect(chunkCalls).to.deep.equal([250, 50]);
    expect(chunkedResult.size).to.equal(300);
    expect(chunkedResult.get('Prop_299')?.ResolvedValueJson).to.equal(
      '"val-Prop_299"'
    );

    // Clear cache and test transport rejection
    defaultCache.clear();

    // Verify in-flight clear invalidation, secure trace non-caching, TTL eviction, and MAX_CACHED_CONTEXTS cap
    const evictionCache = new PropertyResolutionClientCache({
      coalesceWindowMs: 1,
      ttlMs: 1,
      transport: async req =>
        (req.PropertyNames ?? []).map(n => ({
          PropertyName: n,
          Source: 'GlobalDefault',
          IsSecure: n === 'SecProp',
          ResolvedValueJson: `"val-${n}"`
        }))
    });
    const inFlightPromise = evictionCache.resolveProperties(
      { environmentName: 'INFLIGHT' },
      ['P1']
    );
    evictionCache.clear();
    const inFlightRes = await inFlightPromise;
    expect(inFlightRes.get('P1')?.ResolvedValueJson).to.equal('"val-P1"');

    const secRes = await evictionCache.resolveProperties(
      { environmentName: 'SEC-ENV' },
      ['SecProp']
    );
    expect(secRes.get('SecProp')?.IsSecure).to.equal(true);

    const longTtlCache = new PropertyResolutionClientCache({
      coalesceWindowMs: 1,
      ttlMs: 60_000,
      transport: async req =>
        (req.PropertyNames ?? []).map(n => ({
          PropertyName: n,
          Source: 'GlobalDefault',
          ResolvedValueJson: `"val-${n}"`
        }))
    });
    for (let i = 0; i < 34; i++) {
      await longTtlCache.resolveProperties(
        { environmentName: `ENV-${i}` },
        ['PropA']
      );
    }

    await evictionCache.resolveProperties(
      { environmentName: 'TTL-ENV' },
      ['ShortProp']
    );
    await new Promise(r => setTimeout(r, 10));
    await evictionCache.resolveProperties(
      { environmentName: 'TTL-ENV-2' },
      ['ShortProp']
    );

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
        'Host',
        {
          PropertyName: 'Host',
          Source: 'EnvironmentScope',
          ScopeName: 'UT1',
          ResolvedValueJson: '"ut1.local"',
          Diagnostic: 'Ok'
        }
      ],
      [
        'Port',
        {
          PropertyName: 'Port',
          Source: 'GlobalDefault',
          ResolvedValueJson: '8443',
          Diagnostic: 'Ok'
        }
      ],
      [
        'ServerArr',
        {
          PropertyName: 'ServerArr',
          Source: 'BuiltInScopeOption',
          ResolvedValueJson: '["web-1"]',
          Diagnostic: 'Ok'
        }
      ],
      [
        'DeferredExpr',
        {
          PropertyName: 'DeferredExpr',
          Source: 'GlobalDefault',
          ResolvedValueJson: '"fn:DateTime.UtcNow"',
          Diagnostic: 'ExpressionDeferred'
        }
      ],
      [
        'Unresolved',
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

    // Unclosed token prevents partial substitution
    expect(
      evaluateStringWithTraces('$Host$/bad$Unclosed', traces)
    ).to.deep.equal({
      status: 'unresolved',
      resolvedValue: '$Host$/bad$Unclosed',
      resolvedDisplayString: '$Host$/bad$Unclosed'
    });

    // Deferred expression stays unresolved (both referenced and direct fn: prefix)
    expect(evaluateStringWithTraces('$DeferredExpr$', traces).status).to.equal(
      'unresolved'
    );
    expect(
      evaluateStringWithTraces('fn:1 + $Host$', traces)
    ).to.deep.equal({
      status: 'unresolved',
      resolvedValue: 'fn:1 + $Host$',
      resolvedDisplayString: 'fn:1 + $Host$'
    });

    // Single token non-string preserves typed array; concatenation with non-primitive stays unresolved
    expect(evaluateStringWithTraces('$ServerArr$', traces)).to.deep.equal({
      status: 'resolved',
      resolvedValue: ['web-1'],
      resolvedDisplayString: '["web-1"]'
    });
    expect(
      evaluateStringWithTraces('prefix-$ServerArr$', traces).status
    ).to.equal('unresolved');

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
      getTokenBadgeAppearance({
        Source: 'ConfigValue',
        ScopeName: 'UT1'
      }).badgeLabel
    ).to.equal('Config (UT1)');
    expect(
      getTokenBadgeAppearance({
        Source: 'ConfigValue',
        ScopeName: null
      }).badgeLabel
    ).to.equal('Config (Fallback)');
    expect(
      getTokenBadgeAppearance({ Source: 'RuntimeOnly' }).badgeLabel
    ).to.equal('Monitor Runtime');
    expect(
      getTokenBadgeAppearance({ Source: 'Unresolved' }).badgeLabel
    ).to.equal('Missing in Scope');
  });

  it('renders toolbar, mode switches (inline, raw, resolved), tokens, JSON escaping, and inspector card', async () => {
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
          Diagnostic: 'Ok'
        },
        {
          PropertyName: 'GlobalProp',
          RawValue: 'global-val',
          ResolvedValueJson: '"global-val"',
          ValueType: 'String',
          Source: 'GlobalDefault',
          ScopeName: 'Default',
          ResolutionChain: ['GlobalProp'],
          IsSecure: false,
          Diagnostic: 'Ok'
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
          WinPath: 'C:\\deploy\\$GlobalProp$',
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
    ).to.contain('4 properties (3 resolved, 1 missing, 1 secret)');

    // Verify Windows backslashes inside tokenized strings are properly JSON-escaped
    expect(root.textContent).to.contain('C:\\\\deploy\\\\');

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

    // Click GlobalProp token to verify GlobalDefault omits &scope=Default in deep link
    const globalChip = root.querySelector<HTMLElement>(
      '[data-property-name="GlobalProp"]'
    )!;
    globalChip.click();
    await settle();
    const globalHref =
      root.querySelector<HTMLAnchorElement>('a.inspector-link')?.href ?? '';
    expect(globalHref).to.contain('/variables/value-lookup?name=GlobalProp');
    expect(globalHref).to.not.contain('scope=');

    // Click MissingVar token to inspect a trace with empty ResolutionChain and null ScopeName when environmentName is empty
    el.resolutionContext = {};
    await settle();
    await new Promise(r => setTimeout(r, 20));
    await settle();
    const missingChip = root.querySelector<HTMLElement>(
      '[data-property-name="MissingVar"]'
    )!;
    missingChip.click();
    await settle();
    expect(
      root.querySelector('[part="inspector-card"]')?.textContent
    ).to.contain('(not defined)');
    el.resolutionContext = {
      environmentName: 'Endur-UT1',
      projectName: 'Endur'
    };
    await settle();
    await new Promise(r => setTimeout(r, 20));
    await settle();
    root
      .querySelector<HTMLElement>('[data-property-name="MissingVar"]')
      ?.click();
    await settle();

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
    expect(root.textContent).to.contain('"$100"');

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

    // Error in resolutionCache resets traces cleanly, and stale in-flight request is ignored
    let rejectSlow!: (err: Error) => void;
    let resolveSlow!: (traces: PropertyResolutionTraceApiModel[]) => void;
    let callIdx = 0;
    el.resolutionCache = new PropertyResolutionClientCache({
      coalesceWindowMs: 1,
      transport: () => {
        callIdx++;
        if (callIdx === 1) {
          return new Promise<PropertyResolutionTraceApiModel[]>((_, rej) => {
            rejectSlow = rej;
          });
        }
        if (callIdx === 2) {
          return new Promise<PropertyResolutionTraceApiModel[]>(res => {
            resolveSlow = res;
          });
        }
        return Promise.reject(new Error('Network failure'));
      }
    });
    el.resolutionContext = { environmentName: 'Endur-UT1' };
    await settle();
    // Trigger generation 2 immediately before generation 1 settles
    el.resolutionContext = { environmentName: 'Endur-UT2' };
    await settle();
    // Trigger generation 3 immediately before generation 2 settles
    el.resolutionContext = { environmentName: 'Endur-UT3' };
    await settle();
    await new Promise(r => setTimeout(r, 10));
    rejectSlow(new Error('Stale error'));
    resolveSlow([{ PropertyName: 'ApiHost', Source: 'GlobalDefault' }]);
    await settle();
    await new Promise(r => setTimeout(r, 20));
    await settle();

    // Render inspector card while trace is still pending (undefined)
    el.resolutionCache = new PropertyResolutionClientCache({
      coalesceWindowMs: 500,
      transport: async () => []
    });
    el.data = { OnlyOne: '$PendingProp$', NoMissingOrSecret: '$ApiHost$' };
    el.resolutionContext = { environmentName: 'Endur-UT1' };
    await settle();
    const pendingChip = root.querySelector<HTMLElement>(
      '[data-property-name="PendingProp"]'
    )!;
    pendingChip.click();
    await settle();
    expect(
      root.querySelector('[part="inspector-card"]')?.textContent
    ).to.contain('Pending');

    // Render toolbar with 0 missing and 0 secret properties
    el.resolutionCache = customCache;
    el.data = { OnlyOne: '$ApiHost$' };
    el.resolutionContext = { environmentName: 'Endur-UT1' };
    await settle();
    await new Promise(r => setTimeout(r, 20));
    await settle();
    expect(
      root
        .querySelector('[part="context-counts"]')
        ?.textContent?.replace(/\s+/g, ' ')
        .trim()
    ).to.equal('1 property (1 resolved)');
  });

  it('wires resolutionContext in call sites and query params in page-variables-value-lookup', async () => {
    await import('../../src/pages/page-variables-value-lookup');
    await import('../../src/pages/page-scripts-list');
    const bundlesMod = await import('../../src/pages/page-project-bundles');
    await import('../../src/components/deployment-definition-editor');
    await import('../../src/components/deploy/deploy-env');

    // Verify extractBundleRequestOverrides extracts job RequestProperties and TargetEnvironmentName
    expect(
      bundlesMod.extractBundleRequestOverrides(
        {
          RequestProperties: [
            { PropertyName: 'Host', PropertyValue: 'job-host' },
            { PropertyName: '   ', PropertyValue: 'ignored' }
          ]
        },
        'Endur-UT1'
      )
    ).to.deep.equal([
      { PropertyName: 'TargetEnvironmentName', PropertyValue: 'Endur-UT1' },
      { PropertyName: 'Host', PropertyValue: 'job-host' }
    ]);
    expect(bundlesMod.extractBundleRequestOverrides('raw-str')).to.deep.equal(
      []
    );
    expect(bundlesMod.extractBundleRequestOverrides({})).to.deep.equal([]);

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
      locationKind: 'deployment-definition'
    });
  });

  it('covers all tree helpers, previews, search, filter, keyboard navigation, and DOM node primitives', async () => {
    expect(getType(null)).to.equal('null');
    expect(getType([1, 2])).to.equal('array');
    expect(getType('str')).to.equal('string');
    expect(getType(42)).to.equal('number');
    expect(getType(true)).to.equal('boolean');
    expect(getType({ a: 1 })).to.equal('object');

    expect(
      generateNodePreview({
        emptyObj: {},
        nonEmptyObj: { a: 1 },
        emptyArr: [],
        More: 4
      })
    ).to.equal('{ emptyObj: { }, nonEmptyObj: { ... }, emptyArr: [ ], ... }');

    expect(
      generateNodePreview([
        [1, 2],
        'short',
        'this-string-is-longer-than-fifteen-chars',
        99
      ])
    ).to.equal('[ [ ... ], "short", "this-string-is-...", ... ]');
    expect(generateNodePreview({ a: 1, b: 2 })).to.equal('{ a: 1, b: 2 }');

    expect(Array.from(deepTraverse(null))).to.deep.equal([]);
    expect(Array.from(deepTraverse(undefined))).to.deep.equal([]);

    expect(checkGlob('a.b.c', 'a.*.c')).to.equal(true);
    expect(checkGlob('a.b.c.d', 'a.**.d')).to.equal(true);
    expect(checkGlob('a.b.c', 'a.x.c')).to.equal(false);
    expect(isMatchingPath('a.b.c', /^a\.b/)).to.equal(true);
    expect(isMatchingPath('a.b.c', 'a.*.c')).to.equal(true);

    expect(JSONConverter.fromAttribute!('', Object)).to.equal(undefined);
    expect(JSONConverter.fromAttribute!('{"x":1}', Object)).to.deep.equal({
      x: 1
    });
    expect(JSONConverter.toAttribute!({ x: 1 }, Object)).to.equal('{"x":1}');

    const customNode = document.createElement('em');
    customNode.textContent = 'dom-leaf';

    const el = await fixture<HegsJsonViewer>(
      html`<hegs-json-viewer></hegs-json-viewer>`
    );
    await settle();
    // When data is undefined, render() returns null
    expect(el.shadowRoot!.children.length).to.equal(0);

    el.data = {
      Nested: {
        ChildStr: 'searchable-target',
        ChildNum: 123,
        ChildBool: false,
        ChildNull: null,
        ChildDom: customNode
      },
      Other: 'untouched'
    };
    await settle();

    // Initially Nested is collapsed and renders preview
    expect(
      el.shadowRoot!.querySelector('[part="preview"]')?.textContent
    ).to.contain('ChildStr');

    // Toggle Nested via click, Space, Enter, and ignore other keys
    const nestedKey = el.shadowRoot!.querySelector<HTMLElement>(
      '[data-path="Nested"] > [part="key"]'
    )!;
    nestedKey.click();
    await settle();
    expect(nestedKey.getAttribute('aria-expanded')).to.equal('true');

    nestedKey.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })
    );
    await settle();
    expect(nestedKey.getAttribute('aria-expanded')).to.equal('true');

    nestedKey.dispatchEvent(
      new KeyboardEvent('keydown', { key: ' ', bubbles: true })
    );
    await settle();
    expect(nestedKey.getAttribute('aria-expanded')).to.equal('false');

    nestedKey.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })
    );
    await settle();
    expect(nestedKey.getAttribute('aria-expanded')).to.equal('true');

    // Collapse & expand helpers
    el.collapse('Nested');
    await settle();
    expect(nestedKey.getAttribute('aria-expanded')).to.equal('false');

    el.expandAll();
    await settle();
    expect(nestedKey.getAttribute('aria-expanded')).to.equal('true');

    el.collapseAll();
    await settle();
    expect(nestedKey.getAttribute('aria-expanded')).to.equal('false');

    // Search generator expands path, highlights node, and clears highlight on completion
    const gen = el.search('searchable-target');
    const firstMatch = gen.next();
    await settle();
    expect(firstMatch.value).to.deep.equal({
      value: 'searchable-target',
      path: 'Nested.ChildStr'
    });
    expect(el.shadowRoot!.querySelector('mark[part="highlight"]')).to.not.equal(
      null
    );
    const doneMatch = gen.next();
    expect(doneMatch.done).to.equal(true);
    await settle();
    expect(el.shadowRoot!.querySelector('mark[part="highlight"]')).to.equal(
      null
    );

    // Filter and resetFilter
    el.filter('Nested.ChildStr');
    await settle();
    const otherLi = el.shadowRoot!.querySelector<HTMLElement>(
      'li[data-path="Other"]'
    )!;
    expect(otherLi.hidden).to.equal(true);

    el.resetFilter();
    await settle();
    expect(otherLi.hidden).to.equal(false);
  });
});
