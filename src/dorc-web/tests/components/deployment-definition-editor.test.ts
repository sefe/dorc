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
        Items: [
          { EnvironmentName: 'STP UT 01' },
          { EnvironmentName: 'STP PR 01' }
        ]
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
        'STP_DAS_Paper_DestinationPath',
        {
          PropertyName: 'STP_DAS_Paper_DestinationPath',
          Source: 'Environment',
          ScopeName: 'STP UT 01',
          ResolvedValueJson: '"\\\\\\\\stp-ut-01\\\\E$\\\\Apps\\\\DAS_Paper"'
        }
      ],
      [
        'Secret_Token',
        {
          PropertyName: 'Secret_Token',
          Source: 'Environment',
          Diagnostic: 'Masked'
        }
      ],
      [
        'Missing_Var',
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
      evaluateParameterPreview('${stp_das_paper_destinationpath}', traceMap)
    ).to.deep.include({
      status: 'unresolved',
      resolvedText: '${stp_das_paper_destinationpath}'
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
      ResolvedValueJson:
        '"\\\\\\\\$ServerNames_App$\\\\E$\\\\Apps\\\\DAS_Paper"'
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
    expect(evaluateParameterPreview('${Secret_Var}', traceMap)).to.deep.include(
      {
        status: 'masked',
        resolvedText: '•••••••• (Secure)'
      }
    );

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
    const rawInspector = editor.shadowRoot!.querySelector<HTMLDetailsElement>(
      '.raw-json-inspector'
    );
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
    const customRows =
      editor.shadowRoot!.querySelectorAll<HTMLElement>('tr.custom-arg-row');
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

    const renamedKeyInput = rowsAfterFirstChar[
      rowsAfterFirstChar.length - 1
    ].querySelector<TextField>(
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
        '{"ScriptPath":"00 Generic\\\\CopyAndExtractArtifacts.ps1","GenericArguments":[{"DestinationToCopyPath":"${STP_DAS_Paper_DestinationPath}"},{"CustomSavedArg":"val"}]}'
    };
    await editor.updateComplete;
    // Configured DestinationToCopyPath is shown in the table, while unset optional params are collapsed into chips
    expect(
      editor.shadowRoot!.querySelector('tr[data-param="DestinationToCopyPath"]')
    ).not.to.equal(null);
    expect(
      editor.shadowRoot!.querySelector('tr[data-param="buildName"]')
    ).to.equal(null);
    const buildNameChip = [
      ...editor.shadowRoot!.querySelectorAll<HTMLButtonElement>(
        '.optional-param-chip'
      )
    ].find(btn => btn.textContent?.includes('buildName'));
    expect(buildNameChip).not.to.equal(undefined);
    buildNameChip!.click();
    await editor.updateComplete;
    expect(
      editor.shadowRoot!.querySelector('tr[data-param="buildName"]')
    ).not.to.equal(null);

    // Saved custom argument renders as a fixed code label until Rename is clicked
    const renameBtn = editor.shadowRoot!.querySelector<HTMLButtonElement>(
      'button[aria-label="Rename parameter CustomSavedArg"]'
    );
    expect(renameBtn).not.to.equal(null);
    renameBtn!.click();
    await editor.updateComplete;
    const renameKeyInput = editor.shadowRoot!.querySelector<HTMLInputElement>(
      'tr.custom-arg-row vaadin-text-field[aria-label="Additional argument name"]'
    );
    expect(renameKeyInput).not.to.equal(null);
    // Clearing key while renaming keeps the row intact without deleting the saved argument
    renameKeyInput!.value = '';
    renameKeyInput!.dispatchEvent(new Event('input'));
    await editor.updateComplete;
    expect(
      editor.shadowRoot!.querySelectorAll('tr.custom-arg-row').length
    ).to.equal(1);
    // Typing a duplicate template parameter name shows an inline error
    renameKeyInput!.value = 'buildName';
    renameKeyInput!.dispatchEvent(new Event('input'));
    await editor.updateComplete;
    expect(
      editor.shadowRoot!.querySelector('.param-syntax-hint')?.textContent
    ).to.include('already a template parameter');
    // Typing a valid new name updates the saved argument key
    renameKeyInput!.value = 'RenamedCustomArg';
    renameKeyInput!.dispatchEvent(new Event('input'));
    await editor.updateComplete;
    expect(latestPatch[latestPatch.length - 1].ScriptPath).to.include(
      '"RenamedCustomArg"'
    );

    // Clear DestinationToCopyPath
    const clearDestBtn = editor.shadowRoot!.querySelector<HTMLElement>(
      'vaadin-button[aria-label="Clear DestinationToCopyPath"]'
    );
    expect(clearDestBtn).not.to.equal(null);
    clearDestBtn!.click();
    await editor.updateComplete;
    expect(latestPatch[latestPatch.length - 1].ScriptPath).not.to.include(
      '"DestinationToCopyPath"'
    );

    // Saved components render Execution type as an icon badge instead of a disabled combo-box
    const execBadge = editor.shadowRoot!.querySelector<HTMLElement>(
      '.execution-type-badge'
    );
    expect(execBadge).not.to.equal(null);
    expect(execBadge!.getAttribute('data-type')).to.equal('PowerShell');
    expect(
      execBadge!.querySelector('vaadin-icon')?.getAttribute('icon')
    ).to.equal('brand:powershell');
    expect(
      editor.shadowRoot!.querySelector(
        'vaadin-combo-box[label="Execution type"]'
      )
    ).to.equal(null);

    // Unsaved draft components (ComponentId: 0) render the Execution type combo-box
    editor.component = {
      ComponentId: 0,
      ComponentName: 'New Step',
      ComponentType: ComponentType.PowerShell,
      ScriptPath: ''
    };
    await editor.updateComplete;
    expect(editor.shadowRoot!.querySelector('.execution-type-badge')).to.equal(
      null
    );
    const execCombo = editor.shadowRoot!.querySelector<HTMLElement>(
      'vaadin-combo-box[label="Execution type"]'
    );
    expect(execCombo).not.to.equal(null);
    execCombo!.dispatchEvent(
      new CustomEvent('value-changed', {
        detail: { value: ComponentType.Terraform }
      })
    );
    await editor.updateComplete;
    expect(latestPatch[latestPatch.length - 1].ComponentType).to.equal(
      ComponentType.Terraform
    );

    // Saved Terraform component renders brand:terraform icon badge
    editor.component = {
      ComponentId: 202,
      ComponentName: 'Terraform Network',
      ComponentType: ComponentType.Terraform
    };
    await editor.updateComplete;
    const tfBadge = editor.shadowRoot!.querySelector<HTMLElement>(
      '.execution-type-badge'
    );
    expect(tfBadge?.getAttribute('data-type')).to.equal('Terraform');
    expect(
      tfBadge?.querySelector('vaadin-icon')?.getAttribute('icon')
    ).to.equal('brand:terraform');
  });
});
