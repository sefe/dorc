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
    AccessControlType,
    AccessSecureApiModel,
    UserElementApiModel,
} from '../models';

export interface AccessControlGetRequest {
    accessControlType?: AccessControlType;
    accessControlName?: string;
}

export interface AccessControlPutRequest {
    accessSecureApiModel?: AccessSecureApiModel;
}

export interface AccessControlSearchUsersGetRequest {
    search?: string;
}

/**
 * no description
 */
export class AccessControlApi extends BaseAPI {

    /**
     */
    accessControlGet({ accessControlType, accessControlName }: AccessControlGetRequest): Observable<AccessSecureApiModel>
    accessControlGet({ accessControlType, accessControlName }: AccessControlGetRequest, opts?: OperationOpts): Observable<AjaxResponse<AccessSecureApiModel>>
    accessControlGet({ accessControlType, accessControlName }: AccessControlGetRequest, opts?: OperationOpts): Observable<AccessSecureApiModel | AjaxResponse<AccessSecureApiModel>> {

        const query: HttpQuery = {};

        if (accessControlType != null) { query['accessControlType'] = accessControlType; }
        if (accessControlName != null) { query['accessControlName'] = accessControlName; }

        return this.request<AccessSecureApiModel>({
            url: '/AccessControl',
            method: 'GET',
            query,
        }, opts?.responseOpts);
    };

    /**
     */
    accessControlPut({ accessSecureApiModel }: AccessControlPutRequest): Observable<AccessSecureApiModel>
    accessControlPut({ accessSecureApiModel }: AccessControlPutRequest, opts?: OperationOpts): Observable<AjaxResponse<AccessSecureApiModel>>
    accessControlPut({ accessSecureApiModel }: AccessControlPutRequest, opts?: OperationOpts): Observable<AccessSecureApiModel | AjaxResponse<AccessSecureApiModel>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        return this.request<AccessSecureApiModel>({
            url: '/AccessControl',
            method: 'PUT',
            headers,
            body: accessSecureApiModel,
        }, opts?.responseOpts);
    };

    /**
     */
    accessControlSearchUsersGet({ search }: AccessControlSearchUsersGetRequest): Observable<Array<UserElementApiModel>>
    accessControlSearchUsersGet({ search }: AccessControlSearchUsersGetRequest, opts?: OperationOpts): Observable<AjaxResponse<Array<UserElementApiModel>>>
    accessControlSearchUsersGet({ search }: AccessControlSearchUsersGetRequest, opts?: OperationOpts): Observable<Array<UserElementApiModel> | AjaxResponse<Array<UserElementApiModel>>> {

        const query: HttpQuery = {};

        if (search != null) { query['search'] = search; }

        return this.request<Array<UserElementApiModel>>({
            url: '/AccessControl/SearchUsers',
            method: 'GET',
            query,
        }, opts?.responseOpts);
    };

}
