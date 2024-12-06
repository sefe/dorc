import { ComboBox } from '@vaadin/combo-box';
import { TextField } from '@vaadin/text-field';

export function clearTextField(
  shadowRoot: ShadowRoot | null,
  textFieldId: string
) {
  const textField = shadowRoot?.getElementById(textFieldId) as TextField;
  if (!textField) {
    return;
  }

  textField.clear();
  //textField.value = "";

  textField.invalid = false;
  textField.errorMessage = null;
}

export function clearComboBox(
  shadowRoot: ShadowRoot | null,
  comboBoxId: string
) {
  const comboBox = shadowRoot?.getElementById(comboBoxId) as ComboBox;
  if (!comboBox) {
    return;
  }

  comboBox.clear();
  comboBox.invalid = false;
  comboBox.errorMessage = null;
}

export function getSelectedItem(
  shadowRoot: ShadowRoot | null,
  comboBoxId: string
): any {
  const comboBox = shadowRoot?.getElementById(comboBoxId) as ComboBox;
  if (!comboBox) {
    return null;
  }
  return comboBox.selectedItem;
}

export function getTextFieldValue(
  shadowRoot: ShadowRoot | null,
  textFieldId: string
): string | undefined {
  const textField = shadowRoot?.getElementById(textFieldId) as TextField;
  if (!textField) {
    return undefined;
  }
  return textField.value;
}
