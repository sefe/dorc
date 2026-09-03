import { AddUserOrGroupBase } from './AddUserOrGroupBase';
import { customElement, property, state } from 'lit/decorators.js';

import { addSqlUserOrGroupTemplate } from './add-sql-user-or-group.template';
import { addUserOrGroupSharedStypes } from './add-user-or-group.shared-styles';

import { LoginType } from './LoginType';
import { LanIdType } from './LanIdType';

import { clearTextField, getTextFieldValue } from './utilities/vaadinHelper';

type UserOrGroupFoundOrFailureCallback = (errorMessage: string) => void;
type UserOrGroupNotFoundCallback = () => void;

@customElement('add-sql-user-or-group')
export class AddSqlUserOrGroup extends AddUserOrGroupBase {
  private readonly loginType: LoginType = LoginType.Sql;

  @property()
  public lanIdType: LanIdType = LanIdType.User;

  @state()
  protected isModelValid: boolean = false;

  protected lanId: string | undefined = undefined;
  @state()
  protected displayName: string | undefined = undefined;
  @state()
  protected isLanIdValid: boolean | undefined = undefined;
  @state()
  protected lanIdErrorMessage: string | null | undefined = undefined;
  @state()
  protected isTeamNameValid: boolean | undefined = undefined;
  @state()
  protected teamNameErrorMessage: string | null | undefined = undefined;
  @state()
  protected isDisplayNameValid: boolean | undefined = undefined;
  @state()
  protected displayNameErrorMessage: string | null | undefined = undefined;

  static styles = addUserOrGroupSharedStypes;

  render = addSqlUserOrGroupTemplate;

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
              foundOrInvalidCallback('Found SQL user is in DOrc already.');
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
              foundOrInvalidCallback('Found SQL group is in DOrc already.');
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
      return;
    }

    const displayName = e.detail.value;

    const errorMessage = this.validateDisplayName(displayName);
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
    if (!displayName || displayName.length <= 0) {
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
    clearTextField(this.shadowRoot, 'team');
    clearTextField(this.shadowRoot, 'system-account-id');
    clearTextField(this.shadowRoot, 'displayName');

    this.lanId = undefined;
    this.displayName = undefined;

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
    const userOrGroupToSave = this.accountInitializer.getEmptyUserOrGroup(
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
    userOrGroupToSave.DisplayName = dispalyName;

    const teamName = getTextFieldValue(this.shadowRoot, 'team')?.trim();
    userOrGroupToSave.Team = teamName;

    super.submit(userOrGroupToSave);
  }
}
