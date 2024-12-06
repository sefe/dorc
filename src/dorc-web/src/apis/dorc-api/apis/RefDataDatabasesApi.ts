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
import { BaseAPI, throwIfNullOrUndefined, encodeURI } from '../runtime';
import type { OperationOpts, HttpHeaders, HttpQuery } from '../runtime';
import type {
    ApiBoolResult,
    DatabaseApiModel,
    GetDatabaseApiModelListResponseDto,
    PagedDataOperators,
} from '../models';

export interface RefDataDatabasesByPagePutRequest {
    page?: number;
    limit?: number;
    pagedDataOperators?: PagedDataOperators;
}

export interface RefDataDatabasesDeleteRequest {
    databaseId?: number;
}

export interface RefDataDatabasesGetRequest {
    name?: string;
    server?: string;
}

export interface RefDataDatabasesIdGetRequest {
    id: number;
}

export interface RefDataDatabasesPostRequest {
    databaseApiModel?: DatabaseApiModel;
}

export interface RefDataDatabasesPutRequest {
    id?: number;
    databaseApiModel?: DatabaseApiModel;
}

/**
 * no description
 */
export class RefDataDatabasesApi extends BaseAPI {

    /**
     */
    refDataDatabasesByPagePut({ page, limit, pagedDataOperators }: RefDataDatabasesByPagePutRequest): Observable<GetDatabaseApiModelListResponseDto>
    refDataDatabasesByPagePut({ page, limit, pagedDataOperators }: RefDataDatabasesByPagePutRequest, opts?: OperationOpts): Observable<AjaxResponse<GetDatabaseApiModelListResponseDto>>
    refDataDatabasesByPagePut({ page, limit, pagedDataOperators }: RefDataDatabasesByPagePutRequest, opts?: OperationOpts): Observable<GetDatabaseApiModelListResponseDto | AjaxResponse<GetDatabaseApiModelListResponseDto>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        const query: HttpQuery = {};

        if (page != null) { query['page'] = page; }
        if (limit != null) { query['limit'] = limit; }

        return this.request<GetDatabaseApiModelListResponseDto>({
            url: '/RefDataDatabases/ByPage',
            method: 'PUT',
            headers,
            query,
            body: pagedDataOperators,
        }, opts?.responseOpts);
    };

    /**
     */
    refDataDatabasesDelete({ databaseId }: RefDataDatabasesDeleteRequest): Observable<ApiBoolResult>
    refDataDatabasesDelete({ databaseId }: RefDataDatabasesDeleteRequest, opts?: OperationOpts): Observable<AjaxResponse<ApiBoolResult>>
    refDataDatabasesDelete({ databaseId }: RefDataDatabasesDeleteRequest, opts?: OperationOpts): Observable<ApiBoolResult | AjaxResponse<ApiBoolResult>> {

        const query: HttpQuery = {};

        if (databaseId != null) { query['databaseId'] = databaseId; }

        return this.request<ApiBoolResult>({
            url: '/RefDataDatabases',
            method: 'DELETE',
            query,
        }, opts?.responseOpts);
    };

    /**
     */
    refDataDatabasesGet({ name, server }: RefDataDatabasesGetRequest): Observable<Array<DatabaseApiModel>>
    refDataDatabasesGet({ name, server }: RefDataDatabasesGetRequest, opts?: OperationOpts): Observable<AjaxResponse<Array<DatabaseApiModel>>>
    refDataDatabasesGet({ name, server }: RefDataDatabasesGetRequest, opts?: OperationOpts): Observable<Array<DatabaseApiModel> | AjaxResponse<Array<DatabaseApiModel>>> {

        const query: HttpQuery = {};

        if (name != null) { query['name'] = name; }
        if (server != null) { query['server'] = server; }

        return this.request<Array<DatabaseApiModel>>({
            url: '/RefDataDatabases',
            method: 'GET',
            query,
        }, opts?.responseOpts);
    };

    /**
     */
    refDataDatabasesIdGet({ id }: RefDataDatabasesIdGetRequest): Observable<DatabaseApiModel>
    refDataDatabasesIdGet({ id }: RefDataDatabasesIdGetRequest, opts?: OperationOpts): Observable<AjaxResponse<DatabaseApiModel>>
    refDataDatabasesIdGet({ id }: RefDataDatabasesIdGetRequest, opts?: OperationOpts): Observable<DatabaseApiModel | AjaxResponse<DatabaseApiModel>> {
        throwIfNullOrUndefined(id, 'id', 'refDataDatabasesIdGet');

        return this.request<DatabaseApiModel>({
            url: '/RefDataDatabases/{id}'.replace('{id}', encodeURI(id)),
            method: 'GET',
        }, opts?.responseOpts);
    };

    /**
     */
    refDataDatabasesPost({ databaseApiModel }: RefDataDatabasesPostRequest): Observable<DatabaseApiModel>
    refDataDatabasesPost({ databaseApiModel }: RefDataDatabasesPostRequest, opts?: OperationOpts): Observable<AjaxResponse<DatabaseApiModel>>
    refDataDatabasesPost({ databaseApiModel }: RefDataDatabasesPostRequest, opts?: OperationOpts): Observable<DatabaseApiModel | AjaxResponse<DatabaseApiModel>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        return this.request<DatabaseApiModel>({
            url: '/RefDataDatabases',
            method: 'POST',
            headers,
            body: databaseApiModel,
        }, opts?.responseOpts);
    };

    /**
     */
    refDataDatabasesPut({ id, databaseApiModel }: RefDataDatabasesPutRequest): Observable<DatabaseApiModel>
    refDataDatabasesPut({ id, databaseApiModel }: RefDataDatabasesPutRequest, opts?: OperationOpts): Observable<AjaxResponse<DatabaseApiModel>>
    refDataDatabasesPut({ id, databaseApiModel }: RefDataDatabasesPutRequest, opts?: OperationOpts): Observable<DatabaseApiModel | AjaxResponse<DatabaseApiModel>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        const query: HttpQuery = {};

        if (id != null) { query['id'] = id; }

        return this.request<DatabaseApiModel>({
            url: '/RefDataDatabases',
            method: 'PUT',
            headers,
            query,
            body: databaseApiModel,
        }, opts?.responseOpts);
    };

}
