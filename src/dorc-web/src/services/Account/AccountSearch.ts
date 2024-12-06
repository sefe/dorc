import { LoginType } from '../../components/add-user-or-group/LoginType';
import { UserSearchResult } from '../../apis/dorc-api/models';
import { GroupSearchResult } from '../../apis/dorc-api/models/GroupSearchResult';
import { AccountApi } from '../../apis/dorc-api/apis/AccountApi';
import { DirectorySearchApi } from '../../apis/dorc-api';

type UserExistsSuccessCallback = (userExists: boolean) => void;
type UserExistsFailureCallback = (error: any) => void;
type UserExistsCompletionCallback = () => void;

type GroupExistsSuccessCallback = (groupExists: boolean) => void;
type GroupExistsFailureCallback = (error: any) => void;
type GroupExistsCompletionCallback = () => void;

type FindUsersSuccessCallback = (foundUsers: UserSearchResult[]) => void;
type FindUsersFailureCallback = (error: any) => void;
type FindUsersCompletionCallback = () => void;

type FindGroupsSuccessCallback = (foundGroups: GroupSearchResult[]) => void;
type FindGroupsFailureCallback = (error: any) => void;
type FindGroupsCompletionCallback = () => void;

export class AccountSearch {
  public userExists(
    lanId: string,
    loginType: LoginType,
    successCallback: UserExistsSuccessCallback,
    failureCallback: UserExistsFailureCallback,
    completionCallback: UserExistsCompletionCallback
  ): void {
    const api = new AccountApi();
    api
      .accountUserExistsGet({
        userLanId: lanId,
        accountType: loginType
      })
      .subscribe({
        next: (value: boolean) => successCallback(value),
        error: (err: any) => {
          failureCallback(err);
          console.log('Error checking user: ' + err);
          throw new Error(err);
        },
        complete: () => {
          completionCallback();
          console.log('Done checking user: ' + lanId);
        }
      });
  }

  public groupExists(
    lanId: string,
    loginType: LoginType,
    successCallback: GroupExistsSuccessCallback,
    failureCallback: GroupExistsFailureCallback,
    completionCallback: GroupExistsCompletionCallback
  ): void {
    const api = new AccountApi();
    api
      .accountGroupExistsGet({
        groupLanId: lanId,
        accountType: loginType
      })
      .subscribe({
        next: (value: boolean) => successCallback(value),
        error: (err: any) => {
          failureCallback(err);
          console.log('Error checking group: ' + err);
          throw new Error(err);
        },
        complete: () => {
          completionCallback();
          console.log('Done checking group: ' + lanId);
        }
      });
  }

  public findUsers(
    searchCriteria: string,
    successCallback: FindUsersSuccessCallback,
    failureCallback: FindUsersFailureCallback,
    completionCallback: FindUsersCompletionCallback
  ): void {
    const api = new DirectorySearchApi();
    api
      .directorySearchUsersGet({
        userSearchCriteria: searchCriteria
      })
      .subscribe({
        next: (value: UserSearchResult[]) => successCallback(value),
        error: (err: any) => {
          failureCallback(err);
          console.error(err);
        },
        complete: () => {
          completionCallback();
          console.log('Done searching users ' + searchCriteria);
        }
      });
  }

  public findGroups(
    searchCriteria: string,
    successCallback: FindGroupsSuccessCallback,
    failureCallback: FindGroupsFailureCallback,
    completionCallback: FindGroupsCompletionCallback
  ): void {
    const api = new DirectorySearchApi();
    api
      .directorySearchGroupsGet({
        groupSearchCriteria: searchCriteria
      })
      .subscribe({
        next: (value: GroupSearchResult[]) => successCallback(value),
        error: (err: any) => {
          failureCallback(err);
          console.error(err);
        },
        complete: () => {
          completionCallback();
          console.log('Done searching groups ' + searchCriteria);
        }
      });
  }
}
