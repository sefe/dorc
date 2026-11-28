import { describe, it, expect, vi, beforeEach } from 'vitest';
import { of } from 'rxjs';

const { confirmCalls, confirmAnswer } = vi.hoisted(() => ({
  confirmCalls: [] as string[],
  confirmAnswer: { value: true }
}));

vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: (message: string) => {
    confirmCalls.push(message);
    return Promise.resolve(confirmAnswer.value);
  }
}));

await import('../../src/pages/page-variables');
await import('../../src/components/grid-button-groups/variable-value-controls');

const settle = async () => {
  for (let i = 0; i < 6; i++) {
    await new Promise(r => requestAnimationFrame(() => r(null)));
  }
};

describe('IsArray variable UI workflow', () => {
  beforeEach(() => {
    confirmCalls.length = 0;
    confirmAnswer.value = true;
  });

  it('toggles IsArray via confirmation, switches New Variable Value to the Array Items builder, and serializes JSON array on add', async () => {
    const { PropertiesApi, PropertyValuesApi } = await import(
      '../../src/apis/dorc-api'
    );
    const putPropertySpy = vi
      .spyOn(PropertiesApi.prototype, 'propertiesPut')
      .mockReturnValue(
        of([
          {
            Status: 'success',
            Item: { Id: 10, Name: 'TargetHosts', Secure: false, IsArray: true }
          }
        ] as any)
      );
    const postPropertyValuesSpy = vi
      .spyOn(PropertyValuesApi.prototype, 'propertyValuesPost')
      .mockReturnValue(
        of([{ Status: 'success', Item: { Id: 101 } }] as any)
      );
    const getPropertyValuesSpy = vi
      .spyOn(PropertyValuesApi.prototype, 'propertyValuesGet')
      .mockReturnValue(of([] as any));

    const page = document.createElement('page-variables') as any;
    page.isAdmin = true;
    document.body.appendChild(page);
    await settle();

    page.existingPropertySelected = true;
    page.properties = [{ Id: 10, Name: 'TargetHosts', Secure: false, IsArray: false }];
    page.propertyName = 'TargetHosts';
    await settle();

    const isArrayCheckbox = page.shadowRoot?.querySelector(
      '#is-variable-array'
    ) as HTMLElement & { checked: boolean };
    expect(isArrayCheckbox).not.toBeNull();
    isArrayCheckbox.checked = false;
    await settle();

    // Cancel confirmation first -> checkbox reverts to false
    confirmAnswer.value = false;
    (isArrayCheckbox.querySelector('input') as HTMLInputElement).click();
    await settle();
    expect(confirmCalls.length).toBe(1);
    expect(confirmCalls[0]).toContain(
      'mark property "TargetHosts" as an Array variable?'
    );
    expect(isArrayCheckbox.checked).toBe(false);
    expect(putPropertySpy).not.toHaveBeenCalled();

    // Confirm enabling IsArray -> calls propertiesPut and shows Array Items builder
    confirmAnswer.value = true;
    (isArrayCheckbox.querySelector('input') as HTMLInputElement).click();
    await settle();
    expect(putPropertySpy).toHaveBeenCalledWith({
      requestBody: {
        TargetHosts: expect.objectContaining({
          Name: 'TargetHosts',
          IsArray: true
        })
      }
    });
    expect(page.selectedPropertyIsArray).toBe(true);

    const newValCombo = page.shadowRoot?.querySelector(
      '#newVariableValue'
    ) as any;
    const addItemBtn = page.shadowRoot?.querySelector(
      '#add-array-item-btn'
    ) as HTMLElement;
    const addVariableValueBtn = page.shadowRoot?.querySelector(
      '#add-variable-value-btn'
    ) as HTMLElement;
    expect(newValCombo).not.toBeNull();
    expect(addItemBtn).not.toBeNull();
    expect(addVariableValueBtn).not.toBeNull();

    // Add two array items via the friendly builder
    newValCombo.value = 'web01.dv.local';
    addItemBtn.click();
    await settle();

    newValCombo.value = 'web02.dv.local';
    addItemBtn.click();
    await settle();

    expect(page.newArrayItems).toEqual(['web01.dv.local', 'web02.dv.local']);

    const chips = page.shadowRoot?.querySelectorAll(
      '#new-array-items-list .array-item-chip'
    );
    expect(chips?.length).toBe(2);

    // Submit the new variable value and verify JSON array payload is posted
    addVariableValueBtn.click();
    await settle();

    expect(postPropertyValuesSpy).toHaveBeenCalledWith({
      propertyValueDto: [
        expect.objectContaining({
          Value: '["web01.dv.local","web02.dv.local"]',
          Property: expect.objectContaining({
            Name: 'TargetHosts',
            IsArray: true
          })
        })
      ]
    });

    putPropertySpy.mockRestore();
    postPropertyValuesSpy.mockRestore();
    getPropertyValuesSpy.mockRestore();
    page.remove();
  });

  it('renders visual array chips in read mode, removes/adds items in edit mode, and saves JSON array', async () => {
    const { PropertyValuesApi } = await import('../../src/apis/dorc-api');
    const putValuesSpy = vi
      .spyOn(PropertyValuesApi.prototype, 'propertyValuesPut')
      .mockReturnValue(
        of([{ Status: 'success', Item: { Id: 42 } }] as any)
      );

    const control = document.createElement('variable-value-controls') as any;
    control.editing = false;
    control.value = {
      Id: 42,
      Value: '["node-a","node-b"]',
      UserEditable: true,
      Property: { Name: 'ClusterNodes', Secure: false, IsArray: true }
    };
    document.body.appendChild(control);
    await settle();

    // 1. Read mode displays [Array] badge and visual chips
    const badge = control.shadowRoot?.querySelector('.array-type-badge');
    expect(badge?.textContent?.trim()).toBe('[Array]');

    const readChips = control.shadowRoot?.querySelectorAll(
      '.array-chips-preview .array-item-chip'
    );
    expect(readChips?.length).toBe(2);
    expect(readChips?.[0]?.textContent?.trim()).toBe('node-a');
    expect(readChips?.[1]?.textContent?.trim()).toBe('node-b');

    // 2. Enter edit mode and modify items using the inline array editor
    control.editing = true;
    await settle();

    const editChips = control.shadowRoot?.querySelectorAll(
      '.array-items-editor .array-item-chip'
    );
    expect(editChips?.length).toBe(2);

    // Add a third item via the inline draft input
    const inlineDraft = control.shadowRoot?.querySelector(
      '#addArrayItemInput42'
    ) as any;
    const inlineAddBtn = control.shadowRoot?.querySelector(
      '#addArrayItemBtn42'
    ) as HTMLElement;
    expect(inlineDraft).not.toBeNull();
    expect(inlineAddBtn).not.toBeNull();

    inlineDraft.value = 'node-c';
    inlineDraft.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })
    );
    await settle();

    expect(control.value.Value).toBe('["node-a","node-b","node-c"]');

    // Cancel restores the original array items before editing started
    control._editClick();
    control.editing = true;
    await settle();
    control._removeArrayItem(0);
    await settle();
    expect(control.value.Value).toBe('["node-b","node-c"]');
    control._cancelClick();
    control.editing = false;
    await settle();
    expect(control.value.Value).toBe('["node-a","node-b","node-c"]');

    // Re-enter edit mode, type a pending item without clicking + Item, and verify Save auto-commits it
    control._editClick();
    control.editing = true;
    await settle();
    const pendingDraft = control.shadowRoot?.querySelector(
      '#addArrayItemInput42'
    ) as any;
    pendingDraft.value = 'node-d';

    // Save and verify JSON array payload is sent to PUT /PropertyValues
    const saveBtn = control.shadowRoot?.querySelector(
      'vaadin-button[aria-label="Save"]'
    ) as HTMLElement;
    saveBtn.click();
    await settle();

    expect(putValuesSpy).toHaveBeenCalledWith({
      propertyValueDto: [
        expect.objectContaining({
          Id: 42,
          Value: '["node-a","node-b","node-c","node-d"]'
        })
      ]
    });

    // 3. Secure array property uses password input and masks chips & aria-labels
    const secureControl = document.createElement(
      'variable-value-controls'
    ) as any;
    secureControl.value = {
      Id: 43,
      Value: '["secret-one"]',
      UserEditable: true,
      Property: { Name: 'SecretTokens', Secure: true, IsArray: true }
    };
    document.body.appendChild(secureControl);
    await settle();
    secureControl._editClick();
    secureControl.editing = true;
    await settle();

    const secureDraft = secureControl.shadowRoot?.querySelector(
      '#addArrayItemInput43'
    ) as any;
    expect(secureDraft?.tagName.toLowerCase()).toBe('vaadin-password-field');
    secureDraft.value = 'top-secret-item';
    secureControl._appendArrayItem();
    await settle();

    const secureChipText = secureControl.shadowRoot?.querySelector(
      '.array-items-editor .array-item-text'
    );
    const secureRemoveBtn = secureControl.shadowRoot?.querySelector(
      '.array-items-editor .remove-array-item-btn'
    );
    expect(secureChipText?.textContent?.trim()).toBe('••••••');
    expect(secureRemoveBtn?.getAttribute('aria-label')).toBe('Remove item 1');

    putValuesSpy.mockRestore();
    secureControl.remove();
    control.remove();
  });

  it('creates a brand-new array variable after viewing an existing default variable, handles Enter/input text clearing, and appends to or guards existing default values', async () => {
    const { PropertiesApi, PropertyValuesApi } = await import(
      '../../src/apis/dorc-api'
    );
    const { throwError } = await import('rxjs');

    const postPropertySpy = vi
      .spyOn(PropertiesApi.prototype, 'propertiesPost')
      .mockReturnValue(
        of([
          {
            Status: 'success',
            Item: { Id: 99, Name: 'BrandNewArrayVar', Secure: false, IsArray: true }
          }
        ] as any)
      );

    let currentRemoteValues: any[] = [];
    const getPropertyValuesSpy = vi
      .spyOn(PropertyValuesApi.prototype, 'propertyValuesGet')
      .mockImplementation(() => of(currentRemoteValues as any));

    const postPropertyValuesSpy = vi
      .spyOn(PropertyValuesApi.prototype, 'propertyValuesPost')
      .mockImplementation(({ propertyValueDto }: any) => {
        const saved = {
          Id: 501,
          Value: propertyValueDto[0].Value,
          PropertyValueFilter: propertyValueDto[0].PropertyValueFilter ?? null,
          Property: propertyValueDto[0].Property,
          UserEditable: true
        };
        currentRemoteValues = [saved];
        return of([{ Status: 'success', Item: saved }] as any);
      });

    const putPropertyValuesSpy = vi
      .spyOn(PropertyValuesApi.prototype, 'propertyValuesPut')
      .mockImplementation(({ propertyValueDto }: any) => {
        const updated = {
          ...propertyValueDto[0],
          UserEditable: true
        };
        currentRemoteValues = [updated];
        return of([{ Status: 'success', Item: updated }] as any);
      });

    const page = document.createElement('page-variables') as any;
    page.isAdmin = true;
    document.body.appendChild(page);
    await settle();

    // Simulate having previously viewed an existing variable with a (default) value and environments list
    page.environments = ['DEV-01', 'QA-01'];
    page.filteredEnvironments = ['DEV-01', 'QA-01'];
    page.properties = [
      { Id: 1, Name: 'ExistingVar', Secure: false, IsArray: false }
    ];
    page.propertyValues = [
      {
        Id: 10,
        Value: 'old-default-value',
        PropertyValueFilter: null,
        Property: { Id: 1, Name: 'ExistingVar', Secure: false, IsArray: false }
      }
    ];
    page.allPropertyValues = [...page.propertyValues];
    page.newVariableScope = 'DEV-01';
    page.propertyValueScopeOptions = [{ ValueOption: 'opt1' }];

    // Switch to 'Create New' and create BrandNewArrayVar with IsArray = true
    page.radioValue = 'create';
    await settle();

    page.newVariableName = 'BrandNewArrayVar';
    const arrayCheckbox = page.shadowRoot?.querySelector(
      '#variable-array'
    ) as any;
    expect(arrayCheckbox).not.toBeNull();
    arrayCheckbox.checked = true;

    page.createVariable();
    await settle();

    expect(postPropertySpy).toHaveBeenCalledWith({
      propertyApiModel: [
        expect.objectContaining({
          Name: 'BrandNewArrayVar',
          IsArray: true,
          Secure: false
        })
      ]
    });
    expect(page.radioValue).toBe('existing');
    expect(page.propertyName).toBe('BrandNewArrayVar');
    expect(page.selectedPropertyIsArray).toBe(true);
    expect(page.newVariableName).toBe('');
    expect(page.newVariableScope).toBe('');
    expect(page.propertyValueScopeOptions).toEqual([]);
    // loadVariableValues should only be invoked once during auto-selection of the newly created variable
    expect(getPropertyValuesSpy).toHaveBeenCalledTimes(1);

    // Clearing scope in _newVariableValueScopeChanged resets propertyValueScopeOptions
    page.propertyValueScopeOptions = [{ ValueOption: 'temp' }];
    page._newVariableValueScopeChanged({
      target: { value: '' }
    } as unknown as CustomEvent);
    expect(page.propertyValueScopeOptions).toEqual([]);

    const newValCombo = page.shadowRoot?.querySelector(
      '#newVariableValue'
    ) as any;
    expect(newValCombo).not.toBeNull();

    // Non-Enter keydown on #newVariableValue should not stage an item
    newValCombo._inputElementValue = 'item-one';
    newValCombo.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Tab', bubbles: true })
    );
    expect(page.newArrayItems).toEqual([]);

    // Enter keydown when selectedPropertyIsArray is false should not stage an item
    page.selectedPropertyIsArray = false;
    newValCombo.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
    );
    expect(page.newArrayItems).toEqual([]);
    page.selectedPropertyIsArray = true;

    // Clicking + Add Item when input is empty is a no-op
    newValCombo.value = '';
    newValCombo._inputElementValue = '';
    page._addArrayItemClick();
    expect(page.newArrayItems).toEqual([]);
    newValCombo._inputElementValue = 'item-one';

    // Pressing Enter on #newVariableValue when selectedPropertyIsArray is true stages the item and clears _inputElementValue
    newValCombo.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
    );
    await settle();
    expect(page.newArrayItems).toEqual(['item-one']);
    expect(newValCombo.value).toBe('');
    expect(newValCombo._inputElementValue).toBe('');

    // Type a second item into _inputElementValue without clicking '+ Add Item' and click 'Add Variable Value'
    newValCombo._inputElementValue = 'item-two';
    const addVariableValueBtn = page.shadowRoot?.querySelector(
      '#add-variable-value-btn'
    ) as HTMLElement;
    addVariableValueBtn.click();
    await settle();

    // Should have posted the default array value without any duplicate default error and without splicing filteredEnvironments
    expect(postPropertyValuesSpy).toHaveBeenCalledTimes(1);
    expect(postPropertyValuesSpy).toHaveBeenCalledWith({
      propertyValueDto: [
        expect.objectContaining({
          Value: '["item-one","item-two"]',
          PropertyValueFilter: undefined
        })
      ]
    });
    expect(page.filteredEnvironments).toEqual(['DEV-01', 'QA-01']);
    expect(page.newArrayItems).toEqual([]);

    // Now that BrandNewArrayVar has a default array value, adding another item with blank scope appends via PUT
    newValCombo._inputElementValue = '["item-three","item-four"]';
    addVariableValueBtn.click();
    await settle();

    expect(putPropertyValuesSpy).toHaveBeenCalledTimes(1);
    expect(putPropertyValuesSpy).toHaveBeenCalledWith({
      propertyValueDto: [
        expect.objectContaining({
          Id: 501,
          Value: '["item-one","item-two","item-three","item-four"]',
          PropertyValueFilter: undefined
        })
      ]
    });

    // Test non-success response and error response on PUT when appending to default array value
    const errorAlertSpy = vi
      .spyOn(page, 'errorAlert')
      .mockImplementation(() => {});
    putPropertyValuesSpy.mockReturnValueOnce(
      of([{ Status: 'error', Message: 'update failed' }] as any)
    );
    newValCombo.value = 'item-five';
    addVariableValueBtn.click();
    await settle();
    expect(errorAlertSpy).toHaveBeenCalled();

    putPropertyValuesSpy.mockReturnValueOnce(
      throwError(() => new Error('network error'))
    );
    newValCombo.value = 'item-six';
    addVariableValueBtn.click();
    await settle();
    expect(errorAlertSpy).toHaveBeenCalledTimes(2);

    // Attempting to add an empty default array value when a default already exists still shows the duplicate default error notification
    newValCombo.value = '';
    newValCombo._inputElementValue = '';
    page.newArrayItems = [];
    addVariableValueBtn.click();
    await settle();
    let errorNotification = page.shadowRoot?.querySelector(
      'error-notification'
    );
    expect(errorNotification?.getAttribute('errorMessage')).toBe(
      'This variable already contains a default scoped value!'
    );
    errorNotification?.remove();

    // Secure array variable and scalar variable with an existing default value also show the duplicate default error notification
    const brandNewProp = page.properties.find(
      (p: any) => p.Name === 'BrandNewArrayVar'
    );
    brandNewProp.Secure = true;
    newValCombo.value = 'secret-item';
    addVariableValueBtn.click();
    await settle();
    errorNotification = page.shadowRoot?.querySelector('error-notification');
    expect(errorNotification?.getAttribute('errorMessage')).toBe(
      'This variable already contains a default scoped value!'
    );
    errorNotification?.remove();

    brandNewProp.Secure = false;
    brandNewProp.IsArray = false;
    newValCombo.value = 'scalar-val';
    addVariableValueBtn.click();
    await settle();
    errorNotification = page.shadowRoot?.querySelector('error-notification');
    expect(errorNotification?.getAttribute('errorMessage')).toBe(
      'This variable already contains a default scoped value!'
    );
    errorNotification?.remove();

    brandNewProp.IsArray = true;

    // Adding a scoped array value (e.g. 'DEV-01') removes 'DEV-01' from filteredEnvironments (findIndex > -1)
    page.newVariableScope = 'DEV-01';
    newValCombo.value = 'dev-item';
    addVariableValueBtn.click();
    await settle();
    expect(page.filteredEnvironments).toEqual(['QA-01']);

    // Adding a scoped value when filteredEnvironments is undefined handles findIndex === undefined cleanly
    page.filteredEnvironments = undefined;
    page.newVariableScope = 'QA-01';
    newValCombo.value = 'qa-item';
    addVariableValueBtn.click();
    await settle();

    // When newVariableName does not match any property in _propNameValueChanged (including while in 'create' view where #newVariableValue is absent), it resets newVariableName and falls through cleanly
    page.radioValue = 'create';
    await settle();
    page.newVariableName = 'NonExistentProp';
    page._propNameValueChanged({
      target: { value: 'UnknownProp', title: '' }
    } as unknown as CustomEvent);
    expect(page.newVariableName).toBe('');
    expect(page.existingPropertySelected).toBe(false);

    errorAlertSpy.mockRestore();
    postPropertySpy.mockRestore();
    getPropertyValuesSpy.mockRestore();
    postPropertyValuesSpy.mockRestore();
    putPropertyValuesSpy.mockRestore();
    page.remove();
  });
});
