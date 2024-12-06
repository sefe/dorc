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
import type { OperationOpts } from '../runtime';
import type {
    ApiEndpoints,
} from '../models';

/**
 * no description
 */
export class ApiRootApi extends BaseAPI {

    /**
     */
    rootGet(): Observable<ApiEndpoints>
    rootGet(opts?: OperationOpts): Observable<AjaxResponse<ApiEndpoints>>
    rootGet(opts?: OperationOpts): Observable<ApiEndpoints | AjaxResponse<ApiEndpoints>> {
        return this.request<ApiEndpoints>({
            url: '/',
            method: 'GET',
        }, opts?.responseOpts);
    };

}
