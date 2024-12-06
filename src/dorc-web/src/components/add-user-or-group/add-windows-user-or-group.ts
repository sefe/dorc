import { ComboBox } from '@vaadin/combo-box';
import { AddUserOrGroupBase } from './AddUserOrGroupBase';
import { customElement, property, state } from 'lit/decorators.js';

import { addWindowsUserOrGroupTemplate } from './add-windows-user-or-group.template';
import { addUserOrGroupSharedStypes } from './add-user-or-group.shared-styles';

import { LoginType } from './LoginType';
import { LanIdType } from './LanIdType';
import { UserOrGroupSearchResult } from './UserOrGroupSearchResult';
import { UserApiModel, UserSearchResult } from '../../apis/dorc-api/models';
import { GroupSearchResult } from '../../apis/dorc-api/models/GroupSearchResult';

import {
  clearComboBox,
  clearTextField,
  getSelectedItem,
  getTextFieldValue
} from './utilities/vaadinHelper';

type UserOrGroupFoundOrFailureCallback = (errorMessage: string) => void;
type UserOrGroupNotFoundCallback = () => void;

@customElement('add-windows-user-or-group')
export class AddWindowsUserOrGroup extends AddUserOrGroupBase {
  private readonly loginType: LoginType = LoginType.Windows;
  private readonly filterMinimalLength: number = 3;

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

  @state()
  protected isTeamNameValid: boolean | undefined = undefined;
  @state()
  protected teamNameErrorMessage: string | null | undefined = undefined;
  @state()
  protected isSelectedUserOrGroupValid: boolean | undefined = undefined;
  @state()
  protected selectedUserOrGroupErrorMessage: string | null | undefined =
    undefined;

  static styles = addUserOrGroupSharedStypes;

  render = addWindowsUserOrGroupTemplate;

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

    this.validateSelectedUserOrGroup(
      selectedUserOrGroup,
      () => {
        this.isSelectedUserOrGroupValid = true;
        this.isModelValid = this.validateModel();
        this.selectedUserOrGroupErrorMessage = null;
      },
      (errorMessage: string) => {
        this.isSelectedUserOrGroupValid = false;
        this.isModelValid = this.isSelectedUserOrGroupValid;
        this.selectedUserOrGroupErrorMessage = errorMessage;
      }
    );
  }

  private validateSelectedUserOrGroup(
    selectedUserOrGroup: UserOrGroupSearchResult,
    validCallback: UserOrGroupNotFoundCallback,
    foundOrInvalidCallback: UserOrGroupFoundOrFailureCallback
  ): void {
    if (!selectedUserOrGroup.DisplayName) {
      foundOrInvalidCallback('Display Name is empty.');
      return;
    }

    if (
      !selectedUserOrGroup.FullLogonName ||
      selectedUserOrGroup.FullLogonName.length <= 0
    ) {
      foundOrInvalidCallback('LanId is empty.');
      return;
    }

    try {
      if (this.lanIdType == LanIdType.User) {
        this.accountSearch.userExists(
          selectedUserOrGroup.FullLogonName,
          this.loginType,
          userExists => {
            if (userExists) {
              foundOrInvalidCallback('Found Windows user is in DOrc already.');
            } else {
              validCallback();
            }
          },
          () => null,
          () => null
        );
      } else {
        this.accountSearch.groupExists(
          selectedUserOrGroup.FullLogonName,
          this.loginType,
          groupExists => {
            if (groupExists) {
              foundOrInvalidCallback('Found Windows group is in DOrc already.');
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

  protected teamNameChanged(e: CustomEvent): void {
    if (!this.hasUpdated) {
      return;
    }

    const teamName = e.detail.value;

    const errorMessage = this.validateTeamName(teamName);
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

  protected validateModel(): boolean {
    if (this.isSelectedUserOrGroupValid && this.isTeamNameValid) {
      return true;
    }

    return false;
  }

  protected reset(): void {
    clearComboBox(this.shadowRoot, 'windows-id');
    clearTextField(this.shadowRoot, 'win-id-filter');
    clearTextField(this.shadowRoot, 'team');

    this.searchResults = [];
    this.isUserOrGroupListEnabled = false;

    this.isSelectedUserOrGroupValid = undefined;
    this.selectedUserOrGroupErrorMessage = undefined;
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

    const selectedUserOrGroup = getSelectedItem(
      this.shadowRoot,
      'windows-id'
    ) as UserOrGroupSearchResult;
    userOrGroupToSave.LanId = selectedUserOrGroup.FullLogonName?.trim();
    userOrGroupToSave.LoginId = selectedUserOrGroup.FullLogonName?.trim();
    userOrGroupToSave.DisplayName = selectedUserOrGroup.DisplayName?.trim();

    const teamName = getTextFieldValue(this.shadowRoot, 'team')?.trim();
    userOrGroupToSave.Team = teamName;

    super.submit(userOrGroupToSave);
  }
}
