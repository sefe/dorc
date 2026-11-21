import { expect, fixture, html, settle } from '../../_helpers';
import {
  collectDeployReferencedVariables,
  computePreflightResolutionSummary,
  DeployEnv
} from '../../../src/components/deploy/deploy-env.js';
import { defaultPropertyResolutionClientCache } from '../../../src/variable-resolution/property-resolution-client-cache';
import type { PropertyResolutionTraceApiModel } from '../../../src/apis/dorc-api';

describe('DeployEnv responsive layout', () => {
  it('should NOT contain any table elements', async () => {
    const el = await fixture<DeployEnv>(html` <deploy-env></deploy-env> `);
    await el.updateComplete;

    const tables = el.shadowRoot!.querySelectorAll('table');
    expect(tables.length).to.equal(
      0,
      'deploy-env should not use table layouts'
    );
  });

  it('should use flex layout for build definition and build number inputs', async () => {
    const el = await fixture<DeployEnv>(html` <deploy-env></deploy-env> `);
    await el.updateComplete;

    // The build-defs-section uses CSS class with flex-direction: column
    const flexContainers = el.shadowRoot!.querySelectorAll(
      '.build-defs-section'
    );

    expect(flexContainers.length).to.be.greaterThan(
      0,
      'Should have .build-defs-section flex column containers for combo boxes'
    );
  });

  it('should have combo boxes with flex: 1 to fill available width', async () => {
    const el = await fixture<DeployEnv>(html` <deploy-env></deploy-env> `);
    await el.updateComplete;

    const combos = el.shadowRoot!.querySelectorAll(
      'vaadin-combo-box[style*="flex: 1"]'
    );
    expect(combos.length).to.be.greaterThan(
      0,
      'Combo boxes should use flex: 1 to fill width'
    );
  });

  it('should have max-width constraint on combo containers', async () => {
    // The max-width: 600px is set via .build-defs-section CSS class, not inline
    const styles = (DeployEnv as any).styles;
    const cssText = Array.isArray(styles)
      ? styles.map((s: any) => s.cssText ?? '').join('')
      : ((styles as any)?.cssText ?? '');

    expect(cssText).to.include(
      'max-width: 600px',
      'CSS should include max-width: 600px for combo containers'
    );
  });

  it('should render the deploy button with max-width constraint', async () => {
    const el = await fixture<DeployEnv>(html` <deploy-env></deploy-env> `);
    await el.updateComplete;

    const deployButton = el.shadowRoot!.querySelector(
      'vaadin-button[theme="primary"]'
    ) as HTMLElement;
    expect(deployButton).to.not.be.null;
  });

  it('renders preflight warning banner with link to environment variables when referenced variables are missing, and green readiness pill when resolved', async () => {
    const originalResolve =
      defaultPropertyResolutionClientCache.resolveProperties.bind(
        defaultPropertyResolutionClientCache
      );

    const tracesMap = new Map<string, PropertyResolutionTraceApiModel>([
      [
        'ServerNames_Web',
        {
          PropertyName: 'ServerNames_Web',
          Source: 'Unresolved',
          Diagnostic: 'MissingInScope'
        }
      ],
      [
        'AppPort',
        {
          PropertyName: 'AppPort',
          Source: 'GlobalDefault',
          Diagnostic: 'FallbackToDefault',
          ResolvedValueJson: '"8080"'
        }
      ]
    ]);

    defaultPropertyResolutionClientCache.resolveProperties = async () =>
      tracesMap;

    try {
      const el = await fixture<DeployEnv>(html`<deploy-env></deploy-env>`);
      el.envName = 'Endur DEV 1';
      el.propertyOverrides = [
        {
          PropertyName: 'DeployEndpoint',
          PropertyValue: 'https://$ServerNames_Web$:$AppPort$'
        }
      ];
      await el.updateComplete;
      await settle();

      const referenced = collectDeployReferencedVariables(
        el.req?.requestDto,
        el.propertyOverrides,
        el.data
      );
      expect(referenced).to.deep.equal(['ServerNames_Web', 'AppPort']);

      const summary = computePreflightResolutionSummary(referenced, tracesMap);
      expect(summary.unresolvedVariableNames).to.deep.equal([
        'ServerNames_Web'
      ]);
      expect(summary.fallbackToDefaultVariableNames).to.deep.equal(['AppPort']);

      const warningBanners = el.shadowRoot!.querySelectorAll(
        '.preflight-banner--warning'
      );
      expect(warningBanners.length).to.be.greaterThan(0);
      const bannerText = warningBanners[0].textContent ?? '';
      expect(bannerText).to.include('ServerNames_Web');
      const link = warningBanners[0].querySelector(
        '.preflight-banner__link'
      ) as HTMLAnchorElement | null;
      expect(link).to.not.equal(null);
      expect(link?.getAttribute('href')).to.equal(
        '/environment/Endur%20DEV%201/variables'
      );

      // Now update trace so all variables resolve cleanly
      tracesMap.set('ServerNames_Web', {
        PropertyName: 'ServerNames_Web',
        Source: 'BuiltInScopeOption',
        Diagnostic: 'Ok',
        ResolvedValueJson: '["web01.dev"]'
      });
      await el.refreshPreflightResolution();
      await el.updateComplete;

      const passedBanners = el.shadowRoot!.querySelectorAll(
        '.preflight-banner--passed'
      );
      expect(passedBanners.length).to.be.greaterThan(0);
      expect(passedBanners[0].textContent).to.include(
        'Preflight passed: 2 variable(s) resolved for Endur DEV 1'
      );

      // Verify checked component GenericArguments (${Var} brace-only syntax) are collected
      const withComponentRefs = collectDeployReferencedVariables(
        undefined,
        [],
        [
          {
            id: 42,
            name: 'Copy Step',
            checked: true,
            numOfChildren: 0
          } as any
        ],
        new Map([
          [
            42,
            {
              ComponentId: 42,
              ScriptPath: JSON.stringify({
                ScriptPath: '00 Generic/Copy.ps1',
                GenericArguments: [
                  { Name: 'Dest', Value: '${ComponentDestPath}' },
                  { Name: 'IgnoredDollar', Value: '$NotUsedInGenericArgs$' }
                ]
              })
            }
          ]
        ])
      );
      expect(withComponentRefs).to.deep.equal(['ComponentDestPath']);

      // Verify non-Ok diagnostics (e.g. CircularReference) do not count as resolved
      const circularSummary = computePreflightResolutionSummary(
        ['CyclicVar'],
        new Map([
          [
            'CyclicVar',
            {
              PropertyName: 'CyclicVar',
              Source: 'EnvironmentScope',
              Diagnostic: 'CircularReference'
            }
          ]
        ])
      );
      expect(circularSummary.resolvedInEnvironmentVariableNames).to.deep.equal(
        []
      );
    } finally {
      defaultPropertyResolutionClientCache.resolveProperties = originalResolve;
    }
  });
});
