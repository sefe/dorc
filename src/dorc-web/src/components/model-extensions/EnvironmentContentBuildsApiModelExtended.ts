import { EnvironmentContentBuildsApiModel } from '../../apis/dorc-api';

export class EnvironmentContentBuildsApiModelExtended
  implements EnvironmentContentBuildsApiModel
{
  ComponentName?: string | null;

  RequestBuildNum?: string | null;

  RequestId?: number;

  UpdateDate?: string | null;

  State?: string | null;

  UpdatedDate?: Date;
}
