import { Observable } from 'rxjs';
import {
  RefDataRolesApi,
  RefDataUsersApi,
  UserApiModel
} from './apis/dorc-api';
import { dorcApiConfiguration } from './services/dorc-api-configuration';

export default class GlobalCache {
  private static instance: GlobalCache;

  public allUsersResp: Observable<Array<UserApiModel>> | undefined;

  public allUsers: Array<UserApiModel> | undefined;

  public allRolesResp: Observable<Array<string>> | undefined;

  public userRoles!: string[];

  private constructor() {
    const refDataUsersApi = new RefDataUsersApi(dorcApiConfiguration);
    this.allUsersResp = refDataUsersApi.refDataUsersGet();
    this.allUsersResp.subscribe({
      next: (data: Array<UserApiModel>) => {
        this.allUsers = data;
      },
      error: (err: string) => console.error(err),
    });

    const refDataRolesApi = new RefDataRolesApi(dorcApiConfiguration);
    this.allRolesResp = refDataRolesApi.refDataRolesGet();
    this.allRolesResp.subscribe({
      next: (data: string[]) => {
        this.userRoles = data;
      },
      error: (err: string) => console.error(err),
    });
  }

  public static getInstance(): GlobalCache {
    if (!GlobalCache.instance) {
      GlobalCache.instance = new GlobalCache();
    }
    return GlobalCache.instance;
  }
}
