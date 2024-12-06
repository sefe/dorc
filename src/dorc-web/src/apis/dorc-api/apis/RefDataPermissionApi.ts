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
    PermissionDto,
} from '../models';

export interface RefDataPermissionDeleteRequest {
    id?: number;
}

export interface RefDataPermissionPostRequest {
    permissionDto?: PermissionDto;
}

export interface RefDataPermissionPutRequest {
    id?: number;
    permissionDto?: PermissionDto;
}

/**
 * no description
 */
export class RefDataPermissionApi extends BaseAPI {

    /**
     */
    refDataPermissionDelete({ id }: RefDataPermissionDeleteRequest): Observable<void>
    refDataPermissionDelete({ id }: RefDataPermissionDeleteRequest, opts?: OperationOpts): Observable<void | AjaxResponse<void>>
    refDataPermissionDelete({ id }: RefDataPermissionDeleteRequest, opts?: OperationOpts): Observable<void | AjaxResponse<void>> {

        const query: HttpQuery = {};

        if (id != null) { query['id'] = id; }

        return this.request<void>({
            url: '/RefDataPermission',
            method: 'DELETE',
            query,
        }, opts?.responseOpts);
    };

    /**
     */
    refDataPermissionGet(): Observable<Array<PermissionDto>>
    refDataPermissionGet(opts?: OperationOpts): Observable<AjaxResponse<Array<PermissionDto>>>
    refDataPermissionGet(opts?: OperationOpts): Observable<Array<PermissionDto> | AjaxResponse<Array<PermissionDto>>> {
        return this.request<Array<PermissionDto>>({
            url: '/RefDataPermission',
            method: 'GET',
        }, opts?.responseOpts);
    };

    /**
     */
    refDataPermissionPost({ permissionDto }: RefDataPermissionPostRequest): Observable<void>
    refDataPermissionPost({ permissionDto }: RefDataPermissionPostRequest, opts?: OperationOpts): Observable<void | AjaxResponse<void>>
    refDataPermissionPost({ permissionDto }: RefDataPermissionPostRequest, opts?: OperationOpts): Observable<void | AjaxResponse<void>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        return this.request<void>({
            url: '/RefDataPermission',
            method: 'POST',
            headers,
            body: permissionDto,
        }, opts?.responseOpts);
    };

    /**
     */
    refDataPermissionPut({ id, permissionDto }: RefDataPermissionPutRequest): Observable<void>
    refDataPermissionPut({ id, permissionDto }: RefDataPermissionPutRequest, opts?: OperationOpts): Observable<void | AjaxResponse<void>>
    refDataPermissionPut({ id, permissionDto }: RefDataPermissionPutRequest, opts?: OperationOpts): Observable<void | AjaxResponse<void>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        const query: HttpQuery = {};

        if (id != null) { query['id'] = id; }

        return this.request<void>({
            url: '/RefDataPermission',
            method: 'PUT',
            headers,
            query,
            body: permissionDto,
        }, opts?.responseOpts);
    };

}
