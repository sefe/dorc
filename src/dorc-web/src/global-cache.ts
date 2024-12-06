import { Observable } from 'rxjs';
import {
  RefDataRolesApi,
  RefDataUsersApi,
  UserApiModel
} from './apis/dorc-api';

export default class GlobalCache {
  private static instance: GlobalCache;

  public allUsersResp: Observable<Array<UserApiModel>> | undefined;

  public allUsers: Array<UserApiModel> | undefined;

  public allRolesResp: Observable<Array<string>> | undefined;

  public userRoles!: string[];

  private constructor() {
    const refDataUsersApi = new RefDataUsersApi();
    this.allUsersResp = refDataUsersApi.refDataUsersGet();
    this.allUsersResp.subscribe({
      next: (data: Array<UserApiModel>) => {
        this.allUsers = data;
      },
      error: (err: string) => console.error(err),
      complete: () => console.log('global cache finished loading users')
    });

    const refDataRolesApi = new RefDataRolesApi();
    this.allRolesResp = refDataRolesApi.refDataRolesGet();
    this.allRolesResp.subscribe({
      next: (data: string[]) => {
        this.userRoles = data;
      },
      error: (err: string) => console.error(err),
      complete: () => console.log('global cache finished loading user roles')
    });
  }

  public static getInstance(): GlobalCache {
    if (!GlobalCache.instance) {
      GlobalCache.instance = new GlobalCache();
    }
    return GlobalCache.instance;
  }
}
