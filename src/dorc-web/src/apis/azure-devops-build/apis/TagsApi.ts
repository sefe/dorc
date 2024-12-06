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
import type { UpdateTagParameters } from '../models';

export interface TagsAddBuildTagRequest {
  organization: string;
  project: string;
  buildId: number;
  tag: string;
  apiVersion: string;
}

export interface TagsAddBuildTagsRequest {
  organization: string;
  project: string;
  buildId: number;
  apiVersion: string;
  body: Array<string>;
}

export interface TagsAddDefinitionTagRequest {
  organization: string;
  project: string;
  definitionId: number;
  tag: string;
  apiVersion: string;
}

export interface TagsAddDefinitionTagsRequest {
  organization: string;
  project: string;
  definitionId: number;
  apiVersion: string;
  body: Array<string>;
}

export interface TagsDeleteBuildTagRequest {
  organization: string;
  project: string;
  buildId: number;
  tag: string;
  apiVersion: string;
}

export interface TagsDeleteDefinitionTagRequest {
  organization: string;
  project: string;
  definitionId: number;
  tag: string;
  apiVersion: string;
}

export interface TagsDeleteTagRequest {
  organization: string;
  project: string;
  tag: string;
  apiVersion: string;
}

export interface TagsGetBuildTagsRequest {
  organization: string;
  project: string;
  buildId: number;
  apiVersion: string;
}

export interface TagsGetDefinitionTagsRequest {
  organization: string;
  project: string;
  definitionId: number;
  apiVersion: string;
  revision?: number;
}

export interface TagsGetTagsRequest {
  organization: string;
  project: string;
  apiVersion: string;
}

export interface TagsUpdateBuildTagsRequest {
  organization: string;
  project: string;
  buildId: number;
  apiVersion: string;
  body: UpdateTagParameters;
}

export interface TagsUpdateDefinitionTagsRequest {
  organization: string;
  project: string;
  definitionId: number;
  apiVersion: string;
  body: UpdateTagParameters;
}

/**
 * no description
 */
export class TagsApi extends BaseAPI {
  /**
   * Adds a tag to a build.
   */
  tagsAddBuildTag({
    organization,
    project,
    buildId,
    tag,
    apiVersion
  }: TagsAddBuildTagRequest): Observable<Array<string>>;
  tagsAddBuildTag(
    { organization, project, buildId, tag, apiVersion }: TagsAddBuildTagRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsAddBuildTag(
    { organization, project, buildId, tag, apiVersion }: TagsAddBuildTagRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(organization, 'organization', 'tagsAddBuildTag');
    throwIfNullOrUndefined(project, 'project', 'tagsAddBuildTag');
    throwIfNullOrUndefined(buildId, 'buildId', 'tagsAddBuildTag');
    throwIfNullOrUndefined(tag, 'tag', 'tagsAddBuildTag');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'tagsAddBuildTag');

    const headers: HttpHeaders = {
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

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/builds/{buildId}/tags/{tag}'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{buildId}', encodeURI(buildId))
          .replace('{tag}', encodeURI(tag)),
        method: 'PUT',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Adds tags to a build.
   */
  tagsAddBuildTags({
    organization,
    project,
    buildId,
    apiVersion,
    body
  }: TagsAddBuildTagsRequest): Observable<Array<string>>;
  tagsAddBuildTags(
    {
      organization,
      project,
      buildId,
      apiVersion,
      body
    }: TagsAddBuildTagsRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsAddBuildTags(
    {
      organization,
      project,
      buildId,
      apiVersion,
      body
    }: TagsAddBuildTagsRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(organization, 'organization', 'tagsAddBuildTags');
    throwIfNullOrUndefined(project, 'project', 'tagsAddBuildTags');
    throwIfNullOrUndefined(buildId, 'buildId', 'tagsAddBuildTags');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'tagsAddBuildTags');
    throwIfNullOrUndefined(body, 'body', 'tagsAddBuildTags');

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

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/builds/{buildId}/tags'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{buildId}', encodeURI(buildId)),
        method: 'POST',
        headers,
        query,
        body: body
      },
      opts?.responseOpts
    );
  }

  /**
   * Adds a tag to a definition
   */
  tagsAddDefinitionTag({
    organization,
    project,
    definitionId,
    tag,
    apiVersion
  }: TagsAddDefinitionTagRequest): Observable<Array<string>>;
  tagsAddDefinitionTag(
    {
      organization,
      project,
      definitionId,
      tag,
      apiVersion
    }: TagsAddDefinitionTagRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsAddDefinitionTag(
    {
      organization,
      project,
      definitionId,
      tag,
      apiVersion
    }: TagsAddDefinitionTagRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'tagsAddDefinitionTag'
    );
    throwIfNullOrUndefined(project, 'project', 'tagsAddDefinitionTag');
    throwIfNullOrUndefined(
      definitionId,
      'definitionId',
      'tagsAddDefinitionTag'
    );
    throwIfNullOrUndefined(tag, 'tag', 'tagsAddDefinitionTag');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'tagsAddDefinitionTag');

    const headers: HttpHeaders = {
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

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/definitions/{DefinitionId}/tags/{tag}'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{definitionId}', encodeURI(definitionId))
          .replace('{tag}', encodeURI(tag)),
        method: 'PUT',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Adds multiple tags to a definition.
   */
  tagsAddDefinitionTags({
    organization,
    project,
    definitionId,
    apiVersion,
    body
  }: TagsAddDefinitionTagsRequest): Observable<Array<string>>;
  tagsAddDefinitionTags(
    {
      organization,
      project,
      definitionId,
      apiVersion,
      body
    }: TagsAddDefinitionTagsRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsAddDefinitionTags(
    {
      organization,
      project,
      definitionId,
      apiVersion,
      body
    }: TagsAddDefinitionTagsRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'tagsAddDefinitionTags'
    );
    throwIfNullOrUndefined(project, 'project', 'tagsAddDefinitionTags');
    throwIfNullOrUndefined(
      definitionId,
      'definitionId',
      'tagsAddDefinitionTags'
    );
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'tagsAddDefinitionTags');
    throwIfNullOrUndefined(body, 'body', 'tagsAddDefinitionTags');

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

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/definitions/{DefinitionId}/tags'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{definitionId}', encodeURI(definitionId)),
        method: 'POST',
        headers,
        query,
        body: body
      },
      opts?.responseOpts
    );
  }

  /**
   * Removes a tag from a build. NOTE: This API will not work for tags with special characters. To remove tags with special characters, use the PATCH method instead (in 6.0+)
   */
  tagsDeleteBuildTag({
    organization,
    project,
    buildId,
    tag,
    apiVersion
  }: TagsDeleteBuildTagRequest): Observable<Array<string>>;
  tagsDeleteBuildTag(
    {
      organization,
      project,
      buildId,
      tag,
      apiVersion
    }: TagsDeleteBuildTagRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsDeleteBuildTag(
    {
      organization,
      project,
      buildId,
      tag,
      apiVersion
    }: TagsDeleteBuildTagRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(organization, 'organization', 'tagsDeleteBuildTag');
    throwIfNullOrUndefined(project, 'project', 'tagsDeleteBuildTag');
    throwIfNullOrUndefined(buildId, 'buildId', 'tagsDeleteBuildTag');
    throwIfNullOrUndefined(tag, 'tag', 'tagsDeleteBuildTag');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'tagsDeleteBuildTag');

    const headers: HttpHeaders = {
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

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/builds/{buildId}/tags/{tag}'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{buildId}', encodeURI(buildId))
          .replace('{tag}', encodeURI(tag)),
        method: 'DELETE',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Removes a tag from a definition. NOTE: This API will not work for tags with special characters. To remove tags with special characters, use the PATCH method instead (in 6.0+)
   */
  tagsDeleteDefinitionTag({
    organization,
    project,
    definitionId,
    tag,
    apiVersion
  }: TagsDeleteDefinitionTagRequest): Observable<Array<string>>;
  tagsDeleteDefinitionTag(
    {
      organization,
      project,
      definitionId,
      tag,
      apiVersion
    }: TagsDeleteDefinitionTagRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsDeleteDefinitionTag(
    {
      organization,
      project,
      definitionId,
      tag,
      apiVersion
    }: TagsDeleteDefinitionTagRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'tagsDeleteDefinitionTag'
    );
    throwIfNullOrUndefined(project, 'project', 'tagsDeleteDefinitionTag');
    throwIfNullOrUndefined(
      definitionId,
      'definitionId',
      'tagsDeleteDefinitionTag'
    );
    throwIfNullOrUndefined(tag, 'tag', 'tagsDeleteDefinitionTag');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'tagsDeleteDefinitionTag');

    const headers: HttpHeaders = {
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

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/definitions/{DefinitionId}/tags/{tag}'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{definitionId}', encodeURI(definitionId))
          .replace('{tag}', encodeURI(tag)),
        method: 'DELETE',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Removes a tag from builds, definitions, and from the tag store
   */
  tagsDeleteTag({
    organization,
    project,
    tag,
    apiVersion
  }: TagsDeleteTagRequest): Observable<Array<string>>;
  tagsDeleteTag(
    { organization, project, tag, apiVersion }: TagsDeleteTagRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsDeleteTag(
    { organization, project, tag, apiVersion }: TagsDeleteTagRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(organization, 'organization', 'tagsDeleteTag');
    throwIfNullOrUndefined(project, 'project', 'tagsDeleteTag');
    throwIfNullOrUndefined(tag, 'tag', 'tagsDeleteTag');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'tagsDeleteTag');

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

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/tags/{tag}'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{tag}', encodeURI(tag)),
        method: 'DELETE',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Gets the tags for a build.
   */
  tagsGetBuildTags({
    organization,
    project,
    buildId,
    apiVersion
  }: TagsGetBuildTagsRequest): Observable<Array<string>>;
  tagsGetBuildTags(
    { organization, project, buildId, apiVersion }: TagsGetBuildTagsRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsGetBuildTags(
    { organization, project, buildId, apiVersion }: TagsGetBuildTagsRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(organization, 'organization', 'tagsGetBuildTags');
    throwIfNullOrUndefined(project, 'project', 'tagsGetBuildTags');
    throwIfNullOrUndefined(buildId, 'buildId', 'tagsGetBuildTags');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'tagsGetBuildTags');

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

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/builds/{buildId}/tags'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{buildId}', encodeURI(buildId)),
        method: 'GET',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Gets the tags for a definition.
   */
  tagsGetDefinitionTags({
    organization,
    project,
    definitionId,
    apiVersion,
    revision
  }: TagsGetDefinitionTagsRequest): Observable<Array<string>>;
  tagsGetDefinitionTags(
    {
      organization,
      project,
      definitionId,
      apiVersion,
      revision
    }: TagsGetDefinitionTagsRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsGetDefinitionTags(
    {
      organization,
      project,
      definitionId,
      apiVersion,
      revision
    }: TagsGetDefinitionTagsRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'tagsGetDefinitionTags'
    );
    throwIfNullOrUndefined(project, 'project', 'tagsGetDefinitionTags');
    throwIfNullOrUndefined(
      definitionId,
      'definitionId',
      'tagsGetDefinitionTags'
    );
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'tagsGetDefinitionTags');

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

    if (revision != null) {
      query['revision'] = revision;
    }

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/definitions/{DefinitionId}/tags'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{definitionId}', encodeURI(definitionId)),
        method: 'GET',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Gets a list of all build tags in the project.
   */
  tagsGetTags({
    organization,
    project,
    apiVersion
  }: TagsGetTagsRequest): Observable<Array<string>>;
  tagsGetTags(
    { organization, project, apiVersion }: TagsGetTagsRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsGetTags(
    { organization, project, apiVersion }: TagsGetTagsRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(organization, 'organization', 'tagsGetTags');
    throwIfNullOrUndefined(project, 'project', 'tagsGetTags');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'tagsGetTags');

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

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/tags'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project)),
        method: 'GET',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Adds/Removes tags from a build.
   */
  tagsUpdateBuildTags({
    organization,
    project,
    buildId,
    apiVersion,
    body
  }: TagsUpdateBuildTagsRequest): Observable<Array<string>>;
  tagsUpdateBuildTags(
    {
      organization,
      project,
      buildId,
      apiVersion,
      body
    }: TagsUpdateBuildTagsRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsUpdateBuildTags(
    {
      organization,
      project,
      buildId,
      apiVersion,
      body
    }: TagsUpdateBuildTagsRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(organization, 'organization', 'tagsUpdateBuildTags');
    throwIfNullOrUndefined(project, 'project', 'tagsUpdateBuildTags');
    throwIfNullOrUndefined(buildId, 'buildId', 'tagsUpdateBuildTags');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'tagsUpdateBuildTags');
    throwIfNullOrUndefined(body, 'body', 'tagsUpdateBuildTags');

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

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/builds/{buildId}/tags'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{buildId}', encodeURI(buildId)),
        method: 'PATCH',
        headers,
        query,
        body: body
      },
      opts?.responseOpts
    );
  }

  /**
   * Adds/Removes tags from a definition.
   */
  tagsUpdateDefinitionTags({
    organization,
    project,
    definitionId,
    apiVersion,
    body
  }: TagsUpdateDefinitionTagsRequest): Observable<Array<string>>;
  tagsUpdateDefinitionTags(
    {
      organization,
      project,
      definitionId,
      apiVersion,
      body
    }: TagsUpdateDefinitionTagsRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  tagsUpdateDefinitionTags(
    {
      organization,
      project,
      definitionId,
      apiVersion,
      body
    }: TagsUpdateDefinitionTagsRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'tagsUpdateDefinitionTags'
    );
    throwIfNullOrUndefined(project, 'project', 'tagsUpdateDefinitionTags');
    throwIfNullOrUndefined(
      definitionId,
      'definitionId',
      'tagsUpdateDefinitionTags'
    );
    throwIfNullOrUndefined(
      apiVersion,
      'apiVersion',
      'tagsUpdateDefinitionTags'
    );
    throwIfNullOrUndefined(body, 'body', 'tagsUpdateDefinitionTags');

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

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/build/definitions/{DefinitionId}/tags'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{definitionId}', encodeURI(definitionId)),
        method: 'PATCH',
        headers,
        query,
        body: body
      },
      opts?.responseOpts
    );
  }
}
