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
    ComponentApiModelTemplateApiModel,
} from '../models';

export interface RefDataComponentsGetRequest {
    id?: string;
}

/**
 * no description
 */
export class RefDataComponentsApi extends BaseAPI {

    /**
     */
    refDataComponentsGet({ id }: RefDataComponentsGetRequest): Observable<ComponentApiModelTemplateApiModel>
    refDataComponentsGet({ id }: RefDataComponentsGetRequest, opts?: OperationOpts): Observable<AjaxResponse<ComponentApiModelTemplateApiModel>>
    refDataComponentsGet({ id }: RefDataComponentsGetRequest, opts?: OperationOpts): Observable<ComponentApiModelTemplateApiModel | AjaxResponse<ComponentApiModelTemplateApiModel>> {

        const query: HttpQuery = {};

        if (id != null) { query['id'] = id; }

        return this.request<ComponentApiModelTemplateApiModel>({
            url: '/RefDataComponents',
            method: 'GET',
            query,
        }, opts?.responseOpts);
    };

}
