// tslint:disable
/**
 * Dorc.Api
 * No description provided (generated by Openapi Generator https://github.com/openapitools/openapi-generator)
 *
 * The version of the OpenAPI document: 1.0
 * 
 *
 * NOTE: This class is auto generated by OpenAPI Generator (https://openapi-generator.tech).
 * https://openapi-generator.tech
 * Do not edit the class manually.
 */

import type { Observable } from 'rxjs';
import type { AjaxResponse } from 'rxjs/ajax';
import { BaseAPI } from '../runtime';
import type { OperationOpts, HttpHeaders, HttpQuery } from '../runtime';
import type {
    GetRefDataAuditListResponseDto,
    PagedDataOperators,
} from '../models';

export interface RefDataProjectAuditPutRequest {
    projectId?: number;
    page?: number;
    limit?: number;
    pagedDataOperators?: PagedDataOperators;
}

/**
 * no description
 */
export class RefDataProjectAuditApi extends BaseAPI {

    /**
     */
    refDataProjectAuditPut({ projectId, page, limit, pagedDataOperators }: RefDataProjectAuditPutRequest): Observable<GetRefDataAuditListResponseDto>
    refDataProjectAuditPut({ projectId, page, limit, pagedDataOperators }: RefDataProjectAuditPutRequest, opts?: OperationOpts): Observable<AjaxResponse<GetRefDataAuditListResponseDto>>
    refDataProjectAuditPut({ projectId, page, limit, pagedDataOperators }: RefDataProjectAuditPutRequest, opts?: OperationOpts): Observable<GetRefDataAuditListResponseDto | AjaxResponse<GetRefDataAuditListResponseDto>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        const query: HttpQuery = {};

        if (projectId != null) { query['projectId'] = projectId; }
        if (page != null) { query['page'] = page; }
        if (limit != null) { query['limit'] = limit; }

        return this.request<GetRefDataAuditListResponseDto>({
            url: '/RefDataProjectAudit',
            method: 'PUT',
            headers,
            query,
            body: pagedDataOperators,
        }, opts?.responseOpts);
    };

}
