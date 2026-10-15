import type { Observable } from 'rxjs';
import { customElement } from 'lit/decorators.js';
import { ApiRegistrationAuditApi, PagedDataOperators } from '../apis/dorc-api';
import { GetComponentAuditListResponseDto } from '../apis/dorc-api/models/GetComponentAuditListResponseDto';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { ComponentAuditPageBase } from './component-audit-page-base';

@customElement('page-api-registrations-audit')
export class PageApiRegistrationsAudit extends ComponentAuditPageBase {
  protected readonly entityHeader = 'API Registration';

  protected readonly restrictQueryParam = 'apiRegistrationId';

  protected fetchAudit(args: {
    restrictToId?: number;
    pagedDataOperators: PagedDataOperators;
    page: number;
    limit: number;
  }): Observable<GetComponentAuditListResponseDto> {
    return new ApiRegistrationAuditApi(
      dorcApiConfiguration
    ).apiRegistrationAuditPut({
      apiRegistrationId: args.restrictToId,
      pagedDataOperators: args.pagedDataOperators,
      page: args.page,
      limit: args.limit
    });
  }
}
