import { vi } from 'vitest';
import { of } from 'rxjs';
import { expect, fixture, html, settle } from '../_helpers';
import { ComponentType } from '../../src/apis/dorc-api';
import '../../src/components/deployment-definition-editor';
import {
  evaluateParameterPreview,
  type DeploymentDefinitionEditor
} from '../../src/components/deployment-definition-editor';
import { defaultPropertyResolutionClientCache } from '../../src/variable-resolution/property-resolution-client-cache';
import type { TextField } from '@vaadin/text-field';

const mocks = vi.hoisted(() => ({
  propertiesGet: vi.fn(() =>
    of([
      { Name: 'STP_DAS_Paper_DestinationPath', Secure: false },
      { Name: 'STP_ServiceAccountPassword', Secure: true }
    ])
  ),
  propertiesPost: vi.fn((req: any) =>
    of([{ Status: 'success', Item: req.propertyApiModel?.[0] ?? {} }])
  ),
  propertyValuesPost: vi.fn(() => of([{ Status: 'success', Item: {} }])),
  resolvePreviewPost: vi.fn(() =>
    of([
      {
        PropertyName: 'STP_DAS_Paper_DestinationPath',
        Source: 'Environment',
        ScopeName: 'UT02',
        ResolvedValueJson: '"\\\\\\\\stp-ut-01\\\\E$\\\\Apps\\\\DAS_Paper"',
        IsSecure: false
      }
    ])
  )
}));

vi.mock('../../src/apis/dorc-api', async importOriginal => ({
  ...(await importOriginal<Record<string, unknown>>()),
  PowerShellVersionsApi: class {
    powerShellVersionsGet = () => of([{ Value: '5.1' }, { Value: '7.4' }]);
  },
  PropertiesApi: class {
    propertiesGet = mocks.propertiesGet;
    propertiesPost = mocks.propertiesPost;
  },
  PropertyValuesApi: class {
    propertyValuesPost = mocks.propertyValuesPost;
    propertyValuesResolvePreviewPost = mocks.resolvePreviewPost;
    apiPropertyValuesResolvePreviewPost = mocks.resolvePreviewPost;
    propertyValuesScopeOptionsGet = () =>
      of([
        {
          ValueOption: '$EnvironmentName$',
          SampleResolvedValue: 'STP UT 01'
        },
        {
          ValueOption: '$ServerNames_STP_Strategies$',
          SampleResolvedValue: 'stp-ut-01'
        }
      ]);
  },
  RefDataProjectEnvironmentMappingsApi: class {
    refDataProjectEnvironmentMappingsGet = () =>
      of({
        Items: [{ EnvironmentName: 'STP UT 01' }, { EnvironmentName: 'STP PR 01' }]
      });
  }
}));

vi.mock('../../src/apis/dorc-api/apis', async importOriginal => ({
  ...(await importOriginal<Record<string, unknown>>()),
  PropertyValuesApi: class {
    propertyValuesPost = mocks.propertyValuesPost;
    propertyValuesResolvePreviewPost = mocks.resolvePreviewPost;
    apiPropertyValuesResolvePreviewPost = mocks.resolvePreviewPost;
    propertyValuesScopeOptionsGet = () =>
      of([
        {
          ValueOption: '$EnvironmentName$',
          SampleResolvedValue: 'STP UT 01'
        },
        {
          ValueOption: '$ServerNames_STP_Strategies$',
          SampleResolvedValue: 'stp-ut-01'
        }
      ]);
  }
}));

describe('deployment-definition-editor', () => {
  it('supports ${PropertyName} picker autocomplete, inline variable creation, unified arguments grid, and live environment resolution preview', async () => {
    defaultPropertyResolutionClientCache.clear();
    expect(evaluateParameterPreview('plain', new Map())).to.equal(undefined);
    const traceMap = new Map<string, any>([
      [
        'stp_das_paper_destinationpath',
        {
          PropertyName: 'STP_DAS_Paper_DestinationPath',
          Source: 'Environment',
          ScopeName: 'STP UT 01',
          ResolvedValueJson: '"\\\\\\\\stp-ut-01\\\\E$\\\\Apps\\\\DAS_Paper"'
        }
      ],
      [
        'secret_token',
        {
          PropertyName: 'Secret_Token',
          Source: 'Environment',
          Diagnostic: 'Masked'
        }
      ],
      [
        'missing_var',
        {
          PropertyName: 'Missing_Var',
          Source: 'Unresolved'
        }
      ]
    ]);
    expect(
      evaluateParameterPreview('${STP_DAS_Paper_DestinationPath}', traceMap)
    ).to.deep.include({
      status: 'resolved',
      resolvedText: '\\\\stp-ut-01\\E$\\Apps\\DAS_Paper'
    });
    expect(
      evaluateParameterPreview('${Secret_Token}', traceMap)
    ).to.deep.include({
      status: 'masked',
      resolvedText: '•••••••• (Secure)'
    });
    expect(
      evaluateParameterPreview('${Missing_Var}', traceMap)
    ).to.deep.include({
      status: 'unresolved',
      resolvedText: '${Missing_Var}'
    });

    // UnclosedToken trace with lone literal $ (e.g. E$) still resolves effective path
    traceMap.set('Unc_Admin_Share', {
      PropertyName: 'Unc_Admin_Share',
      Source: 'EnvironmentScope',
      ScopeName: 'STP UT 01',
      Diagnostic: 'UnclosedToken',
      RawValue: '\\\\stp-ut-01\\E$\\Apps\\DAS_Paper',
      ResolvedValueJson: '"\\\\\\\\stp-ut-01\\\\E$\\\\Apps\\\\DAS_Paper"'
    });
    expect(
      evaluateParameterPreview('${Unc_Admin_Share}', traceMap)
    ).to.deep.include({
      status: 'resolved',
      resolvedText: '\\\\stp-ut-01\\E$\\Apps\\DAS_Paper'
    });

    // UnclosedToken trace with blocked $ServerNames_X$ yields 'warning' status with effective path
    traceMap.set('Unc_Blocked_Share', {
      PropertyName: 'Unc_Blocked_Share',
      Source: 'EnvironmentScope',
      ScopeName: 'STP UT 01',
      Diagnostic: 'UnclosedToken',
      RawValue: '\\\\$ServerNames_App$\\E$\\Apps\\DAS_Paper',
      ResolvedValueJson: '"\\\\\\\\$ServerNames_App$\\\\E$\\\\Apps\\\\DAS_Paper"'
    });
    expect(
      evaluateParameterPreview('${Unc_Blocked_Share}', traceMap)
    ).to.deep.include({
      status: 'warning',
      resolvedText: '\\\\$ServerNames_App$\\E$\\Apps\\DAS_Paper'
    });

    // Masked trace
    traceMap.set('Secret_Var', {
      PropertyName: 'Secret_Var',
      Source: 'EnvironmentScope',
      IsSecure: true,
      Diagnostic: 'Masked',
      ResolvedValueJson: '"masked"'
    });
    expect(
      evaluateParameterPreview('${Secret_Var}', traceMap)
    ).to.deep.include({
      status: 'masked',
      resolvedText: '•••••••• (Secure)'
    });

    const editor = await fixture<DeploymentDefinitionEditor>(
      html`<deployment-definition-editor
        .project=${{ ProjectId: 7, ProjectName: 'Payments' }}
        .component=${{
          ComponentId: 25,
          ComponentName: 'Copy DAS Paper',
          ComponentType: ComponentType.PowerShell,
          PSVersion: '7.4',
          ScriptPath:
            '{"ScriptPath":"00 Generic\\\\CopyFolder.ps1","GenericArguments":[{"sourcePath":"C:\\\\Build"},{"destinationPath":"${STP_DAS_Paper_DestinationPath}"}]}'
        }}
      ></deployment-definition-editor>`
    );
    await settle();
    await new Promise(r => setTimeout(r, 80));
    await editor.updateComplete;

    // Live resolved preview badge renders inside the Resolved Preview column
    const badge = editor.shadowRoot!.querySelector('.param-preview-badge');
    expect(badge).not.to.equal(null);
    expect(badge?.textContent).to.include('\\\\stp-ut-01\\E$\\Apps\\DAS_Paper');

    // Insert ${PropertyName} via picker into sourcePath
    const latestPatch: any[] = [];
    editor.addEventListener('definition-changed', (e: Event) => {
      const detail = (e as CustomEvent).detail;
      latestPatch.push(detail);
      editor.component = detail;
    });

    editor.insertVariableIntoParameter(
      '00 Generic\\CopyFolder.ps1',
      [
        { key: 'sourcePath', value: '' },
        {
          key: 'destinationPath',
          value: '${STP_DAS_Paper_DestinationPath}'
        }
      ],
      'sourcePath',
      'STP_DAS_Paper_DestinationPath',
      true
    );
    expect(latestPatch.length).to.equal(1);
    expect(latestPatch[0].ScriptPath).to.include(
      '{"sourcePath":"${STP_DAS_Paper_DestinationPath}"}'
    );

    // Toggle per-field {x} variable picker popover and filter/select variable
    editor.toggleParameterPicker('sourcePath');
    await editor.updateComplete;
    const popover = editor.shadowRoot!.querySelector('.param-picker-popover');
    expect(popover).not.to.equal(null);

    const searchInput = popover!.querySelector('vaadin-text-field')!;
    (searchInput as unknown as { value: string }).value = 'STP_DAS';
    searchInput.dispatchEvent(new Event('input'));
    await editor.updateComplete;

    const firstPickerItem =
      popover!.querySelector<HTMLButtonElement>('.param-picker-item');
    firstPickerItem?.click();
    await editor.updateComplete;
    expect(editor.shadowRoot!.querySelector('.param-picker-popover')).to.equal(
      null
    );

    // Toggle open and close
    editor.toggleParameterPicker('sourcePath');
    await editor.updateComplete;
    editor.toggleParameterPicker('sourcePath');
    await editor.updateComplete;

    // Unified Arguments Grid renders template rows + custom argument rows and collapsible Raw JSON Inspector
    const argsTable = editor.shadowRoot!.querySelector('.generic-args-table');
    expect(argsTable).not.to.equal(null);
    const rawInspector =
      editor.shadowRoot!.querySelector<HTMLDetailsElement>('.raw-json-inspector');
    expect(rawInspector).not.to.equal(null);
    expect(rawInspector!.open).to.equal(false);

    // Add a custom argument row via '+ Add argument' button, edit key & value, and remove it
    const addArgBtn = editor.shadowRoot!.querySelector<HTMLElement>(
      '#addCustomGenericArgBtn'
    );
    expect(addArgBtn).not.to.equal(null);
    const initialCustomRowCount =
      editor.shadowRoot!.querySelectorAll<HTMLElement>(
        'tr.custom-arg-row'
      ).length;
    addArgBtn!.click();
    await editor.updateComplete;
    const customRows = editor.shadowRoot!.querySelectorAll<HTMLElement>(
      'tr.custom-arg-row'
    );
    expect(customRows.length).to.equal(initialCustomRowCount + 1);
    const lastCustomRow = customRows[customRows.length - 1];
    const customKeyInput = lastCustomRow.querySelector<TextField>(
      'vaadin-text-field[aria-label="Additional argument name"]'
    )!;
    // Type character-by-character to verify promoted draft does not resurrect as a ghost row
    customKeyInput.value = 'D';
    customKeyInput.dispatchEvent(new Event('input'));
    await editor.updateComplete;
    const rowsAfterFirstChar =
      editor.shadowRoot!.querySelectorAll<HTMLElement>('tr.custom-arg-row');
    expect(rowsAfterFirstChar.length).to.equal(initialCustomRowCount + 1);

    const renamedKeyInput =
      rowsAfterFirstChar[rowsAfterFirstChar.length - 1].querySelector<TextField>(
        'vaadin-text-field[aria-label="Additional argument name"]'
      )!;
    renamedKeyInput.value = 'DestinationToCopyPath';
    renamedKeyInput.dispatchEvent(new Event('input'));
    await editor.updateComplete;
    expect(
      editor.shadowRoot!.querySelectorAll<HTMLElement>('tr.custom-arg-row')
        .length
    ).to.equal(initialCustomRowCount + 1);
    expect(latestPatch[latestPatch.length - 1].ScriptPath).to.include(
      'DestinationToCopyPath'
    );

    // Inline variable creation for destinationPath with initial default/scoped value
    editor.openInlineVariableCreator(
      'destinationPath',
      'STP_DAS_Paper_BackupPath'
    );
    await editor.updateComplete;
    const inlineBox = editor.shadowRoot!.querySelector('.inline-create-box');
    expect(inlineBox).not.to.equal(null);

    (editor as any).inlineNewPropertyDefaultValue = '\\\\stp-ut-01\\Backup';
    editor.createInlineVariableForParameter();
    await settle();

    expect(mocks.propertiesPost.mock.calls.length).to.be.greaterThan(0);
    expect(mocks.propertyValuesPost.mock.calls.length).to.be.greaterThan(0);
    expect(latestPatch[latestPatch.length - 1].ScriptPath).to.include(
      '${STP_DAS_Paper_BackupPath}'
    );

    // Also verify Clear button on a predefined template parameter (e.g. CopyAndExtractArtifacts.ps1 -> buildName)
    editor.component = {
      ComponentId: 101,
      ComponentName: 'Deploy DAS Paper',
      ComponentType: ComponentType.PowerShell,
      ScriptPath:
        '{"ScriptPath":"00 Generic\\\\CopyAndExtractArtifacts.ps1","GenericArguments":[{"buildName":"1.0.0"}]}'
    };
    await editor.updateComplete;
    const clearBuildNameBtn = editor.shadowRoot!.querySelector<HTMLElement>(
      'vaadin-button[aria-label="Clear buildName"]'
    );
    expect(clearBuildNameBtn).not.to.equal(null);
    clearBuildNameBtn!.click();
    await editor.updateComplete;
    expect(latestPatch[latestPatch.length - 1].ScriptPath).not.to.include(
      '"buildName"'
    );
  });
});
