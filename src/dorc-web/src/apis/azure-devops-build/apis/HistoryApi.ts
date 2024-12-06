// tslint:disable
/**
 * Build
 * No description provided (generated by Openapi Generator https://github.com/openapitools/openapi-generator)
 *
 * The version of the OpenAPI document: 6.1-preview
 * Contact: nugetvss@microsoft.com
 *
 * NOTE: This class is auto generated by OpenAPI Generator (https://openapi-generator.tech).
 * https://openapi-generator.tech
 * Do not edit the class manually.
 */

import type { Observable } from 'rxjs';
import type { AjaxResponse } from 'rxjs/ajax';
import type { HttpHeaders, HttpQuery, OperationOpts } from '../runtime';
import { BaseAPI, encodeURI, throwIfNullOrUndefined } from '../runtime';
import type { BuildRetentionHistory } from '../models';

export interface HistoryGetRequest {
  organization: string;
  apiVersion: string;
  daysToLookback?: number;
}

/**
 * no description
 */
export class HistoryApi extends BaseAPI {
  /**
   * Returns the retention history for the project collection. This includes pipelines that have custom retention rules that may prevent the retention job from cleaning them up, runs per pipeline with retention type, files associated with pipelines owned by the collection with retention type, and the number of files per pipeline.
   */
  historyGet({
    organization,
    apiVersion,
    daysToLookback
  }: HistoryGetRequest): Observable<BuildRetentionHistory>;
  historyGet(
    { organization, apiVersion, daysToLookback }: HistoryGetRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<BuildRetentionHistory>>;
  historyGet(
    { organization, apiVersion, daysToLookback }: HistoryGetRequest,
    opts?: OperationOpts
  ): Observable<BuildRetentionHistory | AjaxResponse<BuildRetentionHistory>> {
    throwIfNullOrUndefined(organization, 'organization', 'historyGet');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'historyGet');

    const headers: HttpHeaders = {
      ...(this.configuration.username != null &&
      this.configuration.password != null
        ? {
            Authorization: `Basic ${btoa(
              this.configuration.username + ':' + this.configuration.password
            )}`
          }
        : undefined)
    };

    const query: HttpQuery = {
      // required parameters are used directly since they are already checked by throwIfNullOrUndefined
      'api-version': apiVersion
    };

    if (daysToLookback != null) {
      query['daysToLookback'] = daysToLookback;
    }

    return this.request<BuildRetentionHistory>(
      {
        url: '/{organization}/_apis/build/retention/history'.replace(
          '{organization}',
          encodeURI(organization)
        ),
        method: 'GET',
        headers,
        query
      },
      opts?.responseOpts
    );
  }
}
