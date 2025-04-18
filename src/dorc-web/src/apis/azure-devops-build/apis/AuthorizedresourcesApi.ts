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
import type { DefinitionResourceReference } from '../models';

export interface AuthorizedresourcesAuthorizeProjectResourcesRequest {
  organization: string;
  project: string;
  apiVersion: string;
  body: Array<DefinitionResourceReference>;
}

export interface AuthorizedresourcesListRequest {
  organization: string;
  project: string;
  apiVersion: string;
  type?: string;
  id?: string;
}

/**
 * no description
 */
export class AuthorizedresourcesApi extends BaseAPI {
  /**
   */
  authorizedresourcesAuthorizeProjectResources({
    organization,
    project,
    apiVersion,
    body
  }: AuthorizedresourcesAuthorizeProjectResourcesRequest): Observable<
    Array<DefinitionResourceReference>
  >;
  authorizedresourcesAuthorizeProjectResources(
    {
      organization,
      project,
      apiVersion,
      body
    }: AuthorizedresourcesAuthorizeProjectResourcesRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<DefinitionResourceReference>>>;
  authorizedresourcesAuthorizeProjectResources(
    {
      organization,
      project,
      apiVersion,
      body
    }: AuthorizedresourcesAuthorizeProjectResourcesRequest,
    opts?: OperationOpts
  ): Observable<
    | Array<DefinitionResourceReference>
    | AjaxResponse<Array<DefinitionResourceReference>>
  > {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'authorizedresourcesAuthorizeProjectResources'
    );
    throwIfNullOrUndefined(
      project,
      'project',
      'authorizedresourcesAuthorizeProjectResources'
    );
    throwIfNullOrUndefined(
      apiVersion,
      'apiVersion',
      'authorizedresourcesAuthorizeProjectResources'
    );
    throwIfNullOrUndefined(
      body,
      'body',
      'authorizedresourcesAuthorizeProjectResources'
    );

    const headers: HttpHeaders = {
      'Content-Type': 'application/json',
      // oauth required
      ...(this.configuration.accessToken != null
        ? {
            Authorization:
              typeof this.configuration.accessToken === 'function'
                ? this.configuration.accessToken('oauth2', [
                    'vso.build_execute'
                  ])
                : this.configuration.accessToken
          }
        : undefined)
    };

    const query: HttpQuery = {
      // required parameters are used directly since they are already checked by throwIfNullOrUndefined
      'api-version': apiVersion
    };

    return this.request<Array<DefinitionResourceReference>>(
      {
        url: '/{organization}/{project}/_apis/build/authorizedresources'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project)),
        method: 'PATCH',
        headers,
        query,
        body: body
      },
      opts?.responseOpts
    );
  }

  /**
   */
  authorizedresourcesList({
    organization,
    project,
    apiVersion,
    type,
    id
  }: AuthorizedresourcesListRequest): Observable<
    Array<DefinitionResourceReference>
  >;
  authorizedresourcesList(
    {
      organization,
      project,
      apiVersion,
      type,
      id
    }: AuthorizedresourcesListRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<DefinitionResourceReference>>>;
  authorizedresourcesList(
    {
      organization,
      project,
      apiVersion,
      type,
      id
    }: AuthorizedresourcesListRequest,
    opts?: OperationOpts
  ): Observable<
    | Array<DefinitionResourceReference>
    | AjaxResponse<Array<DefinitionResourceReference>>
  > {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'authorizedresourcesList'
    );
    throwIfNullOrUndefined(project, 'project', 'authorizedresourcesList');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'authorizedresourcesList');

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

    if (type != null) {
      query['type'] = type;
    }
    if (id != null) {
      query['id'] = id;
    }

    return this.request<Array<DefinitionResourceReference>>(
      {
        url: '/{organization}/{project}/_apis/build/authorizedresources'
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
