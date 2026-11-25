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
    inlineAddBtn.click();
    await settle();

    expect(control.value.Value).toBe('["node-a","node-b","node-c"]');

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
          Value: '["node-a","node-b","node-c"]'
        })
      ]
    });

    putValuesSpy.mockRestore();
    control.remove();
  });
});
