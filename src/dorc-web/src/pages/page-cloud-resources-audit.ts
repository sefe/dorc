import type { Observable } from 'rxjs';
import { customElement } from 'lit/decorators.js';
import { CloudResourceAuditApi, PagedDataOperators } from '../apis/dorc-api';
import { GetComponentAuditListResponseDto } from '../apis/dorc-api/models/GetComponentAuditListResponseDto';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { ComponentAuditPageBase } from './component-audit-page-base';

@customElement('page-cloud-resources-audit')
export class PageCloudResourcesAudit extends ComponentAuditPageBase {
  protected readonly entityHeader = 'Cloud Resource';

  protected readonly restrictQueryParam = 'cloudResourceId';

  protected fetchAudit(args: {
    restrictToId?: number;
    pagedDataOperators: PagedDataOperators;
    page: number;
    limit: number;
  }): Observable<GetComponentAuditListResponseDto> {
    return new CloudResourceAuditApi(
      dorcApiConfiguration
    ).cloudResourceAuditPut({
      cloudResourceId: args.restrictToId,
      pagedDataOperators: args.pagedDataOperators,
      page: args.page,
      limit: args.limit
    });
  }
}
