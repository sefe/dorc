import { EnvironmentHistoryApiModel } from '../../apis/dorc-api';

export class EnvironmentHistoryApiModelExtended
  implements EnvironmentHistoryApiModel
{
  /**
   * @type {number}
   * @memberof EnvironmentHistoryApiModel
   */
  Id?: number;

  /**
   * @type {string}
   * @memberof EnvironmentHistoryApiModel
   */
  EnvName?: string;

  /**
   * @type {string}
   * @memberof EnvironmentHistoryApiModel
   */
  UpdateDate?: string;

  /**
   * @type {string}
   * @memberof EnvironmentHistoryApiModel
   */
  UpdatedBy?: string;

  /**
   * @type {string}
   * @memberof EnvironmentHistoryApiModel
   */
  UpdateType?: string;

  /**
   * @type {string}
   * @memberof EnvironmentHistoryApiModel
   */
  OldVersion?: string;

  /**
   * @type {string}
   * @memberof EnvironmentHistoryApiModel
   */
  NewVersion?: string;

  /**
   * @type {string}
   * @memberof EnvironmentHistoryApiModel
   */
  TfsId?: string;

  /**
   * @type {string}
   * @memberof EnvironmentHistoryApiModel
   */
  Comment?: string;

  UpdatedDate: Date | undefined;
}
