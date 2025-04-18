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
    EnvironmentHistoryApiModel,
} from '../models';

export interface RefDataEnvironmentsHistoryGetRequest {
    id?: number;
}

export interface RefDataEnvironmentsHistoryPutRequest {
    environmentHistoryApiModel?: EnvironmentHistoryApiModel;
}

/**
 * no description
 */
export class RefDataEnvironmentsHistoryApi extends BaseAPI {

    /**
     */
    refDataEnvironmentsHistoryGet({ id }: RefDataEnvironmentsHistoryGetRequest): Observable<Array<EnvironmentHistoryApiModel>>
    refDataEnvironmentsHistoryGet({ id }: RefDataEnvironmentsHistoryGetRequest, opts?: OperationOpts): Observable<AjaxResponse<Array<EnvironmentHistoryApiModel>>>
    refDataEnvironmentsHistoryGet({ id }: RefDataEnvironmentsHistoryGetRequest, opts?: OperationOpts): Observable<Array<EnvironmentHistoryApiModel> | AjaxResponse<Array<EnvironmentHistoryApiModel>>> {

        const query: HttpQuery = {};

        if (id != null) { query['id'] = id; }

        return this.request<Array<EnvironmentHistoryApiModel>>({
            url: '/RefDataEnvironmentsHistory',
            method: 'GET',
            query,
        }, opts?.responseOpts);
    };

    /**
     */
    refDataEnvironmentsHistoryPut({ environmentHistoryApiModel }: RefDataEnvironmentsHistoryPutRequest): Observable<void>
    refDataEnvironmentsHistoryPut({ environmentHistoryApiModel }: RefDataEnvironmentsHistoryPutRequest, opts?: OperationOpts): Observable<void | AjaxResponse<void>>
    refDataEnvironmentsHistoryPut({ environmentHistoryApiModel }: RefDataEnvironmentsHistoryPutRequest, opts?: OperationOpts): Observable<void | AjaxResponse<void>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        return this.request<void>({
            url: '/RefDataEnvironmentsHistory',
            method: 'PUT',
            headers,
            body: environmentHistoryApiModel,
        }, opts?.responseOpts);
    };

}
