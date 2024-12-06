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

/**
 * no description
 */
export class DeploymentV2Api extends BaseAPI {

    /**
     */
    deploymentV2Get(): Observable<string>
    deploymentV2Get(opts?: OperationOpts): Observable<AjaxResponse<string>>
    deploymentV2Get(opts?: OperationOpts): Observable<string | AjaxResponse<string>> {
        return this.request<string>({
            url: '/DeploymentV2',
            method: 'GET',
        }, opts?.responseOpts);
    };

    /**
     */
    deploymentV2Head(): Observable<string>
    deploymentV2Head(opts?: OperationOpts): Observable<AjaxResponse<string>>
    deploymentV2Head(opts?: OperationOpts): Observable<string | AjaxResponse<string>> {
        return this.request<string>({
            url: '/DeploymentV2',
            method: 'HEAD',
        }, opts?.responseOpts);
    };

}
