import { PropertyApiModel, PropertyValueDto } from '../../apis/dorc-api';

export class PropertyValueDtoExtended implements PropertyValueDto {
  Id?: number;

  Value?: string | null;

  Property?: PropertyApiModel;

  PropertyValueFilter?: string | null;

  PropertyValueFilterId?: number | null;

  Priority?: number;

  DefaultValue?: boolean;

  UserEditable?: boolean;

  IsDuplicate?: boolean;
}
