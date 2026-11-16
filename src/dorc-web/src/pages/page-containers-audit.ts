import type { Observable } from 'rxjs';
import { customElement } from 'lit/decorators.js';
import { ContainerAuditApi, PagedDataOperators } from '../apis/dorc-api';
import { GetComponentAuditListResponseDto } from '../apis/dorc-api/models/GetComponentAuditListResponseDto';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { ComponentAuditPageBase } from './component-audit-page-base';

@customElement('page-containers-audit')
export class PageContainersAudit extends ComponentAuditPageBase {
  protected readonly entityHeader = 'Container';

  protected readonly restrictQueryParam = 'containerId';

  protected fetchAudit(args: {
    restrictToId?: number;
    pagedDataOperators: PagedDataOperators;
    page: number;
    limit: number;
  }): Observable<GetComponentAuditListResponseDto> {
    return new ContainerAuditApi(dorcApiConfiguration).containerAuditPut({
      containerId: args.restrictToId,
      pagedDataOperators: args.pagedDataOperators,
      page: args.page,
      limit: args.limit
    });
  }
}
