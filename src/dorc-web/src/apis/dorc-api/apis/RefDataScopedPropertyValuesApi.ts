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
    GetScopedPropertyValuesResponseDto,
    PagedDataOperators,
} from '../models';

export interface RefDataScopedPropertyValuesPutRequest {
    scope?: string;
    page?: number;
    limit?: number;
    pagedDataOperators?: PagedDataOperators;
}

/**
 * no description
 */
export class RefDataScopedPropertyValuesApi extends BaseAPI {

    /**
     */
    refDataScopedPropertyValuesPut({ scope, page, limit, pagedDataOperators }: RefDataScopedPropertyValuesPutRequest): Observable<GetScopedPropertyValuesResponseDto>
    refDataScopedPropertyValuesPut({ scope, page, limit, pagedDataOperators }: RefDataScopedPropertyValuesPutRequest, opts?: OperationOpts): Observable<AjaxResponse<GetScopedPropertyValuesResponseDto>>
    refDataScopedPropertyValuesPut({ scope, page, limit, pagedDataOperators }: RefDataScopedPropertyValuesPutRequest, opts?: OperationOpts): Observable<GetScopedPropertyValuesResponseDto | AjaxResponse<GetScopedPropertyValuesResponseDto>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        const query: HttpQuery = {};

        if (scope != null) { query['scope'] = scope; }
        if (page != null) { query['page'] = page; }
        if (limit != null) { query['limit'] = limit; }

        return this.request<GetScopedPropertyValuesResponseDto>({
            url: '/RefDataScopedPropertyValues',
            method: 'PUT',
            headers,
            query,
            body: pagedDataOperators,
        }, opts?.responseOpts);
    };

}
