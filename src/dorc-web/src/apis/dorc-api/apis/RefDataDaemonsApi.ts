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
    DaemonApiModel,
} from '../models';

export interface RefDataDaemonsDeleteRequest {
    id?: number;
}

export interface RefDataDaemonsPostRequest {
    daemonApiModel?: DaemonApiModel;
}

export interface RefDataDaemonsPutRequest {
    id?: number;
    daemonApiModel?: DaemonApiModel;
}

/**
 * no description
 */
export class RefDataDaemonsApi extends BaseAPI {

    /**
     */
    refDataDaemonsDelete({ id }: RefDataDaemonsDeleteRequest): Observable<void>
    refDataDaemonsDelete({ id }: RefDataDaemonsDeleteRequest, opts?: OperationOpts): Observable<void | AjaxResponse<void>>
    refDataDaemonsDelete({ id }: RefDataDaemonsDeleteRequest, opts?: OperationOpts): Observable<void | AjaxResponse<void>> {

        const query: HttpQuery = {};

        if (id != null) { query['id'] = id; }

        return this.request<void>({
            url: '/RefDataDaemons',
            method: 'DELETE',
            query,
        }, opts?.responseOpts);
    };

    /**
     */
    refDataDaemonsGet(): Observable<Array<DaemonApiModel>>
    refDataDaemonsGet(opts?: OperationOpts): Observable<AjaxResponse<Array<DaemonApiModel>>>
    refDataDaemonsGet(opts?: OperationOpts): Observable<Array<DaemonApiModel> | AjaxResponse<Array<DaemonApiModel>>> {
        return this.request<Array<DaemonApiModel>>({
            url: '/RefDataDaemons',
            method: 'GET',
        }, opts?.responseOpts);
    };

    /**
     */
    refDataDaemonsPost({ daemonApiModel }: RefDataDaemonsPostRequest): Observable<DaemonApiModel>
    refDataDaemonsPost({ daemonApiModel }: RefDataDaemonsPostRequest, opts?: OperationOpts): Observable<AjaxResponse<DaemonApiModel>>
    refDataDaemonsPost({ daemonApiModel }: RefDataDaemonsPostRequest, opts?: OperationOpts): Observable<DaemonApiModel | AjaxResponse<DaemonApiModel>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        return this.request<DaemonApiModel>({
            url: '/RefDataDaemons',
            method: 'POST',
            headers,
            body: daemonApiModel,
        }, opts?.responseOpts);
    };

    /**
     */
    refDataDaemonsPut({ id, daemonApiModel }: RefDataDaemonsPutRequest): Observable<DaemonApiModel>
    refDataDaemonsPut({ id, daemonApiModel }: RefDataDaemonsPutRequest, opts?: OperationOpts): Observable<AjaxResponse<DaemonApiModel>>
    refDataDaemonsPut({ id, daemonApiModel }: RefDataDaemonsPutRequest, opts?: OperationOpts): Observable<DaemonApiModel | AjaxResponse<DaemonApiModel>> {

        const headers: HttpHeaders = {
            'Content-Type': 'application/json',
        };

        const query: HttpQuery = {};

        if (id != null) { query['id'] = id; }

        return this.request<DaemonApiModel>({
            url: '/RefDataDaemons',
            method: 'PUT',
            headers,
            query,
            body: daemonApiModel,
        }, opts?.responseOpts);
    };

}
