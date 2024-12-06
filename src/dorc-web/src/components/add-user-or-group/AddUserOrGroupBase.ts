import { LitElement } from 'lit';
import { property, state } from 'lit/decorators.js';

import { AccountInitializer } from '../../services/Account/AccountInitializer';
import { AccountSearch } from '../../services/Account/AccountSearch';
import { AccountPersistor } from '../../services/Account/AccountPersistor';
import { UserApiModel } from '../../apis/dorc-api/models';
export abstract class AddUserOrGroupBase extends LitElement {
  protected accountInitializer: AccountInitializer = new AccountInitializer();
  protected accountSearch: AccountSearch = new AccountSearch();
  protected accountPersistor: AccountPersistor = new AccountPersistor();

  @property()
  public createdEventName: string = 'created';

  @state()
  protected overlayMessage: string | null | undefined = null;

  protected abstract reset(): void;
  protected abstract validateModel(): boolean;

  protected submit(userOrGroup: UserApiModel): void {
    this.accountPersistor.saveUserOrGroup(
      userOrGroup,
      (savedUserOrGroup: UserApiModel) => {
        if (!savedUserOrGroup) {
          this.overlayMessage = 'Error creating user or group!';
          return;
        }
        this.dispatchUserOrGroupCreatedEvent(savedUserOrGroup);
        this.reset();
      },
      () => null,
      () => null
    );
  }

  private dispatchUserOrGroupCreatedEvent(data: UserApiModel): void {
    if (data.Id != 0) {
      const event = new CustomEvent(this.createdEventName, {
        detail: {
          userOrGroup: data
        },
        composed: true
      });
      this.dispatchEvent(event);
    } else {
      this.overlayMessage = 'Error adding user or group!';
    }
  }
}
