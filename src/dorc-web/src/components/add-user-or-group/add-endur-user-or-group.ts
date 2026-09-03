import { ComboBox } from '@vaadin/combo-box';
import { AddUserOrGroupBase } from './AddUserOrGroupBase';
import { customElement, property, state } from 'lit/decorators.js';

import { addEndurUserOrGroupTemplate } from './add-endur-user-or-group.template';
import { addUserOrGroupSharedStypes } from './add-user-or-group.shared-styles';

import { LoginType } from './LoginType';
import { LanIdType } from './LanIdType';
import { UserOrGroupSearchResult } from './UserOrGroupSearchResult';
import { UserApiModel, UserSearchResult } from '../../apis/dorc-api/models';
import { GroupSearchResult } from '../../apis/dorc-api/models/GroupSearchResult';

import {
  clearComboBox,
  clearTextField,
  getTextFieldValue
} from './utilities/vaadinHelper';
import { getShortLogonName } from '../../helpers/user-extensions';

type UserOrGroupFoundOrFailureCallback = (errorMessage: string) => void;
type UserOrGroupNotFoundCallback = () => void;

@customElement('add-endur-user-or-group')
export class AddEndurUserOrGroup extends AddUserOrGroupBase {
  private readonly loginType: LoginType = LoginType.Endur;
  private readonly filterMinimalLength: number = 3;
  private readonly displayNameEndurPostfix: string = '[Endur]';

  @property()
  public lanIdType: LanIdType = LanIdType.User;

  @state()
  protected searchResults: UserOrGroupSearchResult[] = [];
  @state()
  protected isUserOrGroupListEnabled: boolean = false;
  @state()
  protected isUserOrGroupLoadingCompleted: boolean = true;

  @state()
  protected isModelValid: boolean = false;

  // Control states:
  @state()
  protected winIdFilter: string | undefined = undefined;
  @state()
  protected lanId: string | undefined = undefined;
  @state()
  protected displayName: string | null | undefined = undefined;
  @state()
  protected isLanIdValid: boolean | undefined = undefined; // "undefined" means that validation is not performed.
  @state()
  protected lanIdErrorMessage: string | null | undefined = undefined; // "undefined" means that validation is not performed. "null" means valid state, error message is not assumed.
  @state()
  protected isTeamNameValid: boolean | undefined = undefined; // "undefined" means that validation is not performed.
  @state()
  protected teamNameErrorMessage: string | null | undefined = undefined; // "undefined" means that validation is not performed. "null" means valid state, error message is not assumed.
  @state()
  protected isDisplayNameValid: boolean | undefined = undefined; // "undefined" means that validation is not performed.
  @state()
  protected displayNameErrorMessage: string | null | undefined = undefined; // "undefined" means that validation is not performed. "null" means valid state, error message is not assumed.

  static styles = addUserOrGroupSharedStypes;

  render = addEndurUserOrGroupTemplate;

  protected filterKeypressed(e: KeyboardEvent) {
    if (e.code !== 'Enter') {
      return;
    }

    this.updateUserOrGroupList();
  }

  protected updateUserOrGroupList() {
    const filterValue = getTextFieldValue(this.shadowRoot, 'win-id-filter');

    if (!filterValue || filterValue.length < this.filterMinimalLength) {
      return;
    }

    this.isUserOrGroupLoadingCompleted = false;
    this.isUserOrGroupListEnabled = this.isUserOrGroupLoadingCompleted;

    switch (this.lanIdType) {
      case LanIdType.User:
        this.accountSearch.findUsers(
          filterValue,
          (foundUsers: UserSearchResult[]) => {
            this.searchResults = foundUsers.map(function (userSearchResult) {
              return {
                DisplayName: userSearchResult.DisplayName,
                FullLogonName: userSearchResult.FullLogonName
              };
            });

            if (this.searchResults.length > 0) {
              this.isUserOrGroupListEnabled = true;
            }
            this.isUserOrGroupLoadingCompleted = true;
          },
          () => null,
          () => null
        );
        break;
      case LanIdType.Group:
        this.accountSearch.findGroups(
          filterValue,
          (foundGroups: GroupSearchResult[]) => {
            this.searchResults = foundGroups.map(function (groupSearchResult) {
              return {
                DisplayName: groupSearchResult.DisplayName,
                FullLogonName: groupSearchResult.FullLogonName
              };
            });

            if (this.searchResults.length > 0) {
              this.isUserOrGroupListEnabled = true;
            }
            this.isUserOrGroupLoadingCompleted = true;
          },
          () => null,
          () => null
        );
        break;
      default:
        break;
    }
  }

  protected filteredUserOrGroupSelected(e: CustomEvent) {
    const filteredUsersOrGroupsComboBox = e.currentTarget as ComboBox;
    const selectedUserOrGroup =
      filteredUsersOrGroupsComboBox.selectedItem as UserOrGroupSearchResult;

    this.lanId = getShortLogonName(selectedUserOrGroup.FullLogonName);
    this.displayName = selectedUserOrGroup.DisplayName;
  }

  protected teamNameChanged(e: CustomEvent): void {
    if (!this.hasUpdated) {
      // The control is the first time rendered. For more detailes see: https://lit.dev/docs/components/lifecycle/#hasupdated
      return;
    }

    const teamName = e.detail.value;

    const errorMessage = this.validateTeamName(teamName); // if "null" is returned, it means that validation is passed successfully, no error message is assumed.
    this.teamNameErrorMessage = errorMessage;

    if (errorMessage) {
      this.isTeamNameValid = false;
      this.isModelValid = this.isTeamNameValid;
      return;
    }

    this.isTeamNameValid = true;
    this.isModelValid = this.validateModel();
  }

  private validateTeamName(teamName: string | undefined): string | null {
    if (!teamName || teamName.length <= 0) {
      return 'Team is not specified.';
    }

    if (!/^[a-zA-Z]+$/.test(teamName)) {
      return "The Team field can't be empty and only [a-zA-Z] are allowed.";
    }

    return null;
  }

  protected lanIdChanged(e: CustomEvent): void {
    if (!this.hasUpdated || !e.detail) {
      return;
    }

    this.validateUserOrGroup(
      e.detail.value,
      () => {
        this.isLanIdValid = true;
        this.isModelValid = this.validateModel();
        this.lanIdErrorMessage = null;
      },
      (errorMessage: string) => {
        this.isLanIdValid = false;
        this.isModelValid = false;
        this.lanIdErrorMessage = errorMessage;
      }
    );
  }

  private validateUserOrGroup(
    lanId: string,
    validCallback: UserOrGroupNotFoundCallback,
    foundOrInvalidCallback: UserOrGroupFoundOrFailureCallback
  ): void {
    if (!lanId || lanId.length <= 0) {
      foundOrInvalidCallback('LanId is empty.');
      return;
    }

    try {
      if (this.lanIdType == LanIdType.User) {
        this.accountSearch.userExists(
          lanId,
          this.loginType,
          userExists => {
            if (userExists) {
              foundOrInvalidCallback('Found Endur user is in DOrc already.');
            } else {
              validCallback();
            }
          },
          () => null,
          () => null
        );
      } else {
        this.accountSearch.groupExists(
          lanId,
          this.loginType,
          groupExists => {
            if (groupExists) {
              foundOrInvalidCallback('Found Endur group is in DOrc already.');
            } else {
              validCallback();
            }
          },
          () => null,
          () => null
        );
      }
    } catch (e) {
      let errorMessage: string = '';
      if (e instanceof Error) errorMessage = e.message;
      this.overlayMessage = errorMessage;
      foundOrInvalidCallback(errorMessage);
    }
  }

  protected displayNameChanged(e: CustomEvent): void {
    if (!this.hasUpdated) {
      // The control is the first time rendered. For more detailes see: https://lit.dev/docs/components/lifecycle/#hasupdated
      return;
    }

    const displayName = e.detail.value + ' ' + this.displayNameEndurPostfix;

    const errorMessage = this.validateDisplayName(displayName); // if "null" is returned, it means that validation is passed successfully, no error message is assumed.
    this.displayNameErrorMessage = errorMessage;

    if (errorMessage) {
      this.isDisplayNameValid = false;
      this.isModelValid = this.isDisplayNameValid;
      return;
    }

    this.isDisplayNameValid = true;
    this.isModelValid = this.validateModel();
  }

  private validateDisplayName(displayName: string | undefined): string | null {
    if (
      !displayName ||
      displayName.length <= (' ' + this.displayNameEndurPostfix).length
    ) {
      return 'Display Name is empty.';
    }

    return null;
  }

  protected validateModel(): boolean {
    if (this.isDisplayNameValid && this.isLanIdValid && this.isTeamNameValid) {
      return true;
    }

    return false;
  }

  protected reset(): void {
    clearComboBox(this.shadowRoot, 'windows-id');
    clearTextField(this.shadowRoot, 'win-id-filter');
    clearTextField(this.shadowRoot, 'team');
    clearTextField(this.shadowRoot, 'system-account-id');
    clearTextField(this.shadowRoot, 'displayName');

    this.winIdFilter = undefined;
    this.lanId = undefined;
    this.displayName = undefined;

    this.searchResults = [];
    this.isUserOrGroupListEnabled = false;

    this.isLanIdValid = undefined;
    this.lanIdErrorMessage = undefined;
    this.isDisplayNameValid = undefined;
    this.displayNameErrorMessage = undefined;
    this.isTeamNameValid = undefined;
    this.teamNameErrorMessage = undefined;

    this.isModelValid = false;
    this.overlayMessage = '';
  }

  protected submit() {
    const userOrGroupToSave: UserApiModel =
      this.accountInitializer.getEmptyUserOrGroup(
        this.loginType,
        this.lanIdType
      );

    const lanId = getTextFieldValue(
      this.shadowRoot,
      'system-account-id'
    )?.trim();
    userOrGroupToSave.LanId = lanId;
    userOrGroupToSave.LoginId = lanId;

    const dispalyName = getTextFieldValue(
      this.shadowRoot,
      'displayName'
    )?.trim();
    userOrGroupToSave.DisplayName =
      dispalyName + ' ' + this.displayNameEndurPostfix;

    const teamName = getTextFieldValue(this.shadowRoot, 'team')?.trim();
    userOrGroupToSave.Team = teamName;

    super.submit(userOrGroupToSave);
  }
}
