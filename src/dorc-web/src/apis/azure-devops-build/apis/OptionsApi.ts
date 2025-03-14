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
import type { BuildOptionDefinition } from '../models';

export interface OptionsListRequest {
  organization: string;
  project: string;
  apiVersion: string;
}

/**
 * no description
 */
export class OptionsApi extends BaseAPI {
  /**
   * Gets all build definition options supported by the system.
   */
  optionsList({
    organization,
    project,
    apiVersion
  }: OptionsListRequest): Observable<Array<BuildOptionDefinition>>;
  optionsList(
    { organization, project, apiVersion }: OptionsListRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<BuildOptionDefinition>>>;
  optionsList(
    { organization, project, apiVersion }: OptionsListRequest,
    opts?: OperationOpts
  ): Observable<
    Array<BuildOptionDefinition> | AjaxResponse<Array<BuildOptionDefinition>>
  > {
    throwIfNullOrUndefined(organization, 'organization', 'optionsList');
    throwIfNullOrUndefined(project, 'project', 'optionsList');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'optionsList');

    const headers: HttpHeaders = {
      // oauth required
      ...(this.configuration.accessToken != null
        ? {
            Authorization:
              typeof this.configuration.accessToken === 'function'
                ? this.configuration.accessToken('oauth2', ['vso.build'])
                : this.configuration.accessToken
          }
        : undefined)
    };

    const query: HttpQuery = {
      // required parameters are used directly since they are already checked by throwIfNullOrUndefined
      'api-version': apiVersion
    };

    return this.request<Array<BuildOptionDefinition>>(
      {
        url: '/{organization}/{project}/_apis/build/options'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project)),
        method: 'GET',
        headers,
        query
      },
      opts?.responseOpts
    );
  }
}
