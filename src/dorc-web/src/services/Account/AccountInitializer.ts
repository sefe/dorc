import { LoginType } from '../../components/add-user-or-group/LoginType';
import { LanIdType } from '../../components/add-user-or-group/LanIdType';
import { UserApiModel } from '../../apis/dorc-api/models';

export class AccountInitializer {
  public getEmptyUserOrGroup(
    loginType: LoginType,
    lanIdType: LanIdType
  ): UserApiModel {
    const userOrGroup: UserApiModel = {
      DisplayName: '',
      LanId: '',
      LoginType: loginType,
      Team: '',
      LanIdType: lanIdType
    };
    return userOrGroup;
  }
}
