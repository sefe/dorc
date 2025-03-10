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
    ServiceStatusApiModel,
} from '../models';

export interface DaemonStatusEnvNameGetRequest {
    envName: string;
}

export interface DaemonStatusGetRequest {
    id?: number;
}

export interface DaemonStatusPutRequest {
    serviceStatusApiModel?: ServiceStatusApiModel;
}

/**
 * no description
 */
export class DaemonStatusApi extends BaseAPI {

    /**
     */
    daemonStatusEnvNameGet({ envName }: DaemonStatusEnvNameGetRequest): Observable<Array<ServiceStatusApiModel>>
    daemonStatusEnvNameGet({ envName }: DaemonStatusEnvNameGetRequest, opts?: OperationOpts): Observable<AjaxResponse<Array<ServiceStatusApiModel>>>
    daemonStatusEnvNameGet({ envName }: DaemonStatusEnvNameGetRequest, opts?: OperationOpts): Observable<Array<ServiceStatusApiModel> | AjaxResponse<Array<ServiceStatusApiModel>>> {
        throwIfNullOrUndefined(envName, 'envName', 'daemonStatusEnvNameGet');

        return this.request<Array<ServiceStatusApiModel>>({
            url: '/DaemonStatus/{envName}'.replace('{envName}', encodeURI(envName)),
            method: 'GET',
        }, opts?.responseOpts);
    };

    /**
     */
    daemonStatusGet({ id }: DaemonStatusGetRequest): Observable<Array<ServiceStatusApiModel>>
    daemonStatusGet({ id }: DaemonStatusGetRequest, opts?: OperationOpts): Observable<AjaxResponse<Array<ServiceStatusApiModel>>>
    daemonStatusGet({ id }: DaemonStatusGetRequest, opts?: OperationOpts): Observable<Array<ServiceStatusApiModel> | AjaxResponse<Array<ServiceStatusApiModel>>> {

        const query: HttpQuery = {};

        if (id != null) { query['id'] = id; }

        return this.request<Array<ServiceStatusApiModel>>({
            url: '/DaemonStatus',
            method: 'GET',
            query,
        }, opts?.responseOpts);
    };

    /**
     */
    daemonStatusPut({ serviceStatusApiModel }: DaemonStatusPutRequest): Observable<ServiceStatusApiModel>
    daemonStatusPut({ serviceStatusApiModel }: DaemonStatusPutRequest, opts?: OperationOpts): Observable<AjaxResponse<ServiceStatusApiModel>>
    daemonStatusPut({ serviceStatusApiModel }: DaemonStatusPutRequest, opts?: OperationOpts): Observable<ServiceStatusApiModel | AjaxResponse<ServiceStatusApiModel>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        return this.request<ServiceStatusApiModel>({
            url: '/DaemonStatus',
            method: 'PUT',
            headers,
            body: serviceStatusApiModel,
        }, opts?.responseOpts);
    };

}
