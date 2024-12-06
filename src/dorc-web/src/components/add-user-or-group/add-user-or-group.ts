import { LitElement } from 'lit';
import { customElement, state } from 'lit/decorators.js';

import { addUserOrGroupTemplate } from './add-user-or-group.template';
import { addUserOrGroupSharedStypes } from './add-user-or-group.shared-styles';

import { LoginType } from './LoginType';
import { LanIdType } from './LanIdType';

@customElement('add-user-or-group')
export class AddUserOrGroup extends LitElement {
  protected createdEventName: string = 'created';
  protected loginTypes: string[] = Object.values(LoginType);
  protected lanIdTypes: string[] = Object.values(LanIdType);

  @state()
  protected selectedLoginType: LoginType | undefined;
  @state()
  protected selectedLanIdType: LanIdType | undefined;

  static styles = addUserOrGroupSharedStypes;

  render = addUserOrGroupTemplate;

  protected loginTypeChanged(e: CustomEvent) {
    this.selectedLoginType = e.detail.value as LoginType;
  }

  protected lanIdTypeChanged(e: CustomEvent) {
    this.selectedLanIdType = e.detail.value as LanIdType;
  }

  protected userOrGroupCreatedHandler(e: CustomEvent) {
    if (!e.detail.userOrGroup) {
      e.preventDefault();
      return;
    }

    const event = new CustomEvent('user-or-group-created', {
      detail: {
        user: e.detail.userOrGroup
      },
      composed: true
    });

    this.dispatchEvent(event);
  }
}
