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
import type { OperationOpts, HttpQuery } from '../runtime';
import type {
    UserPermDto,
} from '../models';

export interface RefDataUserPermissionsDeleteRequest {
    userId?: number;
    permissionId?: number;
    dbId?: number;
    envId?: number;
}

export interface RefDataUserPermissionsGetRequest {
    userId?: number;
    databaseId?: number;
    envId?: number;
}

export interface RefDataUserPermissionsPutRequest {
    userId?: number;
    permissionId?: number;
    dbId?: number;
    envId?: number;
}

/**
 * no description
 */
export class RefDataUserPermissionsApi extends BaseAPI {

    /**
     */
    refDataUserPermissionsDelete({ userId, permissionId, dbId, envId }: RefDataUserPermissionsDeleteRequest): Observable<boolean>
    refDataUserPermissionsDelete({ userId, permissionId, dbId, envId }: RefDataUserPermissionsDeleteRequest, opts?: OperationOpts): Observable<AjaxResponse<boolean>>
    refDataUserPermissionsDelete({ userId, permissionId, dbId, envId }: RefDataUserPermissionsDeleteRequest, opts?: OperationOpts): Observable<boolean | AjaxResponse<boolean>> {

        const query: HttpQuery = {};

        if (userId != null) { query['userId'] = userId; }
        if (permissionId != null) { query['permissionId'] = permissionId; }
        if (dbId != null) { query['dbId'] = dbId; }
        if (envId != null) { query['envId'] = envId; }

        return this.request<boolean>({
            url: '/RefDataUserPermissions',
            method: 'DELETE',
            query,
        }, opts?.responseOpts);
    };

    /**
     */
    refDataUserPermissionsGet({ userId, databaseId, envId }: RefDataUserPermissionsGetRequest): Observable<Array<UserPermDto>>
    refDataUserPermissionsGet({ userId, databaseId, envId }: RefDataUserPermissionsGetRequest, opts?: OperationOpts): Observable<AjaxResponse<Array<UserPermDto>>>
    refDataUserPermissionsGet({ userId, databaseId, envId }: RefDataUserPermissionsGetRequest, opts?: OperationOpts): Observable<Array<UserPermDto> | AjaxResponse<Array<UserPermDto>>> {

        const query: HttpQuery = {};

        if (userId != null) { query['userId'] = userId; }
        if (databaseId != null) { query['databaseId'] = databaseId; }
        if (envId != null) { query['envId'] = envId; }

        return this.request<Array<UserPermDto>>({
            url: '/RefDataUserPermissions',
            method: 'GET',
            query,
        }, opts?.responseOpts);
    };

    /**
     */
    refDataUserPermissionsPut({ userId, permissionId, dbId, envId }: RefDataUserPermissionsPutRequest): Observable<boolean>
    refDataUserPermissionsPut({ userId, permissionId, dbId, envId }: RefDataUserPermissionsPutRequest, opts?: OperationOpts): Observable<AjaxResponse<boolean>>
    refDataUserPermissionsPut({ userId, permissionId, dbId, envId }: RefDataUserPermissionsPutRequest, opts?: OperationOpts): Observable<boolean | AjaxResponse<boolean>> {

        const query: HttpQuery = {};

        if (userId != null) { query['userId'] = userId; }
        if (permissionId != null) { query['permissionId'] = permissionId; }
        if (dbId != null) { query['dbId'] = dbId; }
        if (envId != null) { query['envId'] = envId; }

        return this.request<boolean>({
            url: '/RefDataUserPermissions',
            method: 'PUT',
            query,
        }, opts?.responseOpts);
    };

}
