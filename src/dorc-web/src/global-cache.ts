import { Observable } from 'rxjs';
import { RefDataRolesApi } from './apis/dorc-api';
import { dorcApiConfiguration } from './services/dorc-api-configuration';

export default class GlobalCache {
  private static instance: GlobalCache;

  public allRolesResp: Observable<Array<string>> | undefined;

  public userRoles!: string[];

  private constructor() {
    const refDataRolesApi = new RefDataRolesApi(dorcApiConfiguration);
    this.allRolesResp = refDataRolesApi.refDataRolesGet();
    this.allRolesResp.subscribe({
      next: (data: string[]) => {
        this.userRoles = data;
      },
      error: (err: string) => console.error(err)
    });
  }

  public static getInstance(): GlobalCache {
    if (!GlobalCache.instance) {
      GlobalCache.instance = new GlobalCache();
    }
    return GlobalCache.instance;
  }
}
