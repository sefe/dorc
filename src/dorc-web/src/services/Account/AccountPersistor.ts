import { UserApiModel } from '../../apis/dorc-api/models';
import { RefDataUsersApi } from '../../apis/dorc-api';

type SavedUserOrGroupSuccessCallback = (savedUserOrGroup: UserApiModel) => void;
type SavedUserOrGroupFailureCallback = (error: any) => void;
type SavedUserOrGroupCompletionCallback = () => void;

export class AccountPersistor {
  public saveUserOrGroup(
    userOrGroup: UserApiModel,
    successCallback: SavedUserOrGroupSuccessCallback,
    failureCallback: SavedUserOrGroupFailureCallback,
    completionCallback: SavedUserOrGroupCompletionCallback
  ): void {
    const api = new RefDataUsersApi();
    api.refDataUsersPost({ userApiModel: userOrGroup }).subscribe({
      next: (value: UserApiModel) => successCallback(value),
      error: (err: any) => {
        failureCallback(err);
        console.error(err);
      },
      complete: () => {
        completionCallback();
        console.log('Done adding user');
      }
    });
  }
}
