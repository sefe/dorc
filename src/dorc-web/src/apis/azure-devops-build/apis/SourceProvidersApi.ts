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
import type {
  PullRequest,
  RepositoryWebhook,
  SourceProviderAttributes,
  SourceRepositories,
  SourceRepositoryItem
} from '../models';

export interface SourceProvidersGetFileContentsRequest {
  organization: string;
  project: string;
  providerName: string;
  apiVersion: string;
  serviceEndpointId?: string;
  repository?: string;
  commitOrBranch?: string;
  path?: string;
}

export interface SourceProvidersGetPathContentsRequest {
  organization: string;
  project: string;
  providerName: string;
  apiVersion: string;
  serviceEndpointId?: string;
  repository?: string;
  commitOrBranch?: string;
  path?: string;
}

export interface SourceProvidersGetPullRequestRequest {
  organization: string;
  project: string;
  providerName: string;
  pullRequestId: string;
  apiVersion: string;
  repositoryId?: string;
  serviceEndpointId?: string;
}

export interface SourceProvidersListRequest {
  organization: string;
  project: string;
  apiVersion: string;
}

export interface SourceProvidersListBranchesRequest {
  organization: string;
  project: string;
  providerName: string;
  apiVersion: string;
  serviceEndpointId?: string;
  repository?: string;
  branchName?: string;
}

export interface SourceProvidersListRepositoriesRequest {
  organization: string;
  project: string;
  providerName: string;
  apiVersion: string;
  serviceEndpointId?: string;
  repository?: string;
  resultSet?: SourceProvidersListRepositoriesResultSetEnum;
  pageResults?: boolean;
  continuationToken?: string;
}

export interface SourceProvidersListWebhooksRequest {
  organization: string;
  project: string;
  providerName: string;
  apiVersion: string;
  serviceEndpointId?: string;
  repository?: string;
}

export interface SourceProvidersRestoreWebhooksRequest {
  organization: string;
  project: string;
  providerName: string;
  apiVersion: string;
  body: Array<string>;
  serviceEndpointId?: string;
  repository?: string;
}

/**
 * no description
 */
export class SourceProvidersApi extends BaseAPI {
  /**
   * Gets the contents of a file in the given source code repository.
   */
  sourceProvidersGetFileContents({
    organization,
    project,
    providerName,
    apiVersion,
    serviceEndpointId,
    repository,
    commitOrBranch,
    path
  }: SourceProvidersGetFileContentsRequest): Observable<string>;
  sourceProvidersGetFileContents(
    {
      organization,
      project,
      providerName,
      apiVersion,
      serviceEndpointId,
      repository,
      commitOrBranch,
      path
    }: SourceProvidersGetFileContentsRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<string>>;
  sourceProvidersGetFileContents(
    {
      organization,
      project,
      providerName,
      apiVersion,
      serviceEndpointId,
      repository,
      commitOrBranch,
      path
    }: SourceProvidersGetFileContentsRequest,
    opts?: OperationOpts
  ): Observable<string | AjaxResponse<string>> {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'sourceProvidersGetFileContents'
    );
    throwIfNullOrUndefined(
      project,
      'project',
      'sourceProvidersGetFileContents'
    );
    throwIfNullOrUndefined(
      providerName,
      'providerName',
      'sourceProvidersGetFileContents'
    );
    throwIfNullOrUndefined(
      apiVersion,
      'apiVersion',
      'sourceProvidersGetFileContents'
    );

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

    if (serviceEndpointId != null) {
      query['serviceEndpointId'] = serviceEndpointId;
    }
    if (repository != null) {
      query['repository'] = repository;
    }
    if (commitOrBranch != null) {
      query['commitOrBranch'] = commitOrBranch;
    }
    if (path != null) {
      query['path'] = path;
    }

    return this.request<string>(
      {
        url: '/{organization}/{project}/_apis/sourceProviders/{providerName}/filecontents'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{providerName}', encodeURI(providerName)),
        method: 'GET',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Gets the contents of a directory in the given source code repository.
   */
  sourceProvidersGetPathContents({
    organization,
    project,
    providerName,
    apiVersion,
    serviceEndpointId,
    repository,
    commitOrBranch,
    path
  }: SourceProvidersGetPathContentsRequest): Observable<
    Array<SourceRepositoryItem>
  >;
  sourceProvidersGetPathContents(
    {
      organization,
      project,
      providerName,
      apiVersion,
      serviceEndpointId,
      repository,
      commitOrBranch,
      path
    }: SourceProvidersGetPathContentsRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<SourceRepositoryItem>>>;
  sourceProvidersGetPathContents(
    {
      organization,
      project,
      providerName,
      apiVersion,
      serviceEndpointId,
      repository,
      commitOrBranch,
      path
    }: SourceProvidersGetPathContentsRequest,
    opts?: OperationOpts
  ): Observable<
    Array<SourceRepositoryItem> | AjaxResponse<Array<SourceRepositoryItem>>
  > {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'sourceProvidersGetPathContents'
    );
    throwIfNullOrUndefined(
      project,
      'project',
      'sourceProvidersGetPathContents'
    );
    throwIfNullOrUndefined(
      providerName,
      'providerName',
      'sourceProvidersGetPathContents'
    );
    throwIfNullOrUndefined(
      apiVersion,
      'apiVersion',
      'sourceProvidersGetPathContents'
    );

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

    if (serviceEndpointId != null) {
      query['serviceEndpointId'] = serviceEndpointId;
    }
    if (repository != null) {
      query['repository'] = repository;
    }
    if (commitOrBranch != null) {
      query['commitOrBranch'] = commitOrBranch;
    }
    if (path != null) {
      query['path'] = path;
    }

    return this.request<Array<SourceRepositoryItem>>(
      {
        url: '/{organization}/{project}/_apis/sourceProviders/{providerName}/pathcontents'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{providerName}', encodeURI(providerName)),
        method: 'GET',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Gets a pull request object from source provider.
   */
  sourceProvidersGetPullRequest({
    organization,
    project,
    providerName,
    pullRequestId,
    apiVersion,
    repositoryId,
    serviceEndpointId
  }: SourceProvidersGetPullRequestRequest): Observable<PullRequest>;
  sourceProvidersGetPullRequest(
    {
      organization,
      project,
      providerName,
      pullRequestId,
      apiVersion,
      repositoryId,
      serviceEndpointId
    }: SourceProvidersGetPullRequestRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<PullRequest>>;
  sourceProvidersGetPullRequest(
    {
      organization,
      project,
      providerName,
      pullRequestId,
      apiVersion,
      repositoryId,
      serviceEndpointId
    }: SourceProvidersGetPullRequestRequest,
    opts?: OperationOpts
  ): Observable<PullRequest | AjaxResponse<PullRequest>> {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'sourceProvidersGetPullRequest'
    );
    throwIfNullOrUndefined(project, 'project', 'sourceProvidersGetPullRequest');
    throwIfNullOrUndefined(
      providerName,
      'providerName',
      'sourceProvidersGetPullRequest'
    );
    throwIfNullOrUndefined(
      pullRequestId,
      'pullRequestId',
      'sourceProvidersGetPullRequest'
    );
    throwIfNullOrUndefined(
      apiVersion,
      'apiVersion',
      'sourceProvidersGetPullRequest'
    );

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

    if (repositoryId != null) {
      query['repositoryId'] = repositoryId;
    }
    if (serviceEndpointId != null) {
      query['serviceEndpointId'] = serviceEndpointId;
    }

    return this.request<PullRequest>(
      {
        url: '/{organization}/{project}/_apis/sourceProviders/{providerName}/pullrequests/{pullRequestId}'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{providerName}', encodeURI(providerName))
          .replace('{pullRequestId}', encodeURI(pullRequestId)),
        method: 'GET',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Get a list of source providers and their capabilities.
   */
  sourceProvidersList({
    organization,
    project,
    apiVersion
  }: SourceProvidersListRequest): Observable<Array<SourceProviderAttributes>>;
  sourceProvidersList(
    { organization, project, apiVersion }: SourceProvidersListRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<SourceProviderAttributes>>>;
  sourceProvidersList(
    { organization, project, apiVersion }: SourceProvidersListRequest,
    opts?: OperationOpts
  ): Observable<
    | Array<SourceProviderAttributes>
    | AjaxResponse<Array<SourceProviderAttributes>>
  > {
    throwIfNullOrUndefined(organization, 'organization', 'sourceProvidersList');
    throwIfNullOrUndefined(project, 'project', 'sourceProvidersList');
    throwIfNullOrUndefined(apiVersion, 'apiVersion', 'sourceProvidersList');

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

    return this.request<Array<SourceProviderAttributes>>(
      {
        url: '/{organization}/{project}/_apis/sourceproviders'
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
   * Gets a list of branches for the given source code repository.
   */
  sourceProvidersListBranches({
    organization,
    project,
    providerName,
    apiVersion,
    serviceEndpointId,
    repository,
    branchName
  }: SourceProvidersListBranchesRequest): Observable<Array<string>>;
  sourceProvidersListBranches(
    {
      organization,
      project,
      providerName,
      apiVersion,
      serviceEndpointId,
      repository,
      branchName
    }: SourceProvidersListBranchesRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<string>>>;
  sourceProvidersListBranches(
    {
      organization,
      project,
      providerName,
      apiVersion,
      serviceEndpointId,
      repository,
      branchName
    }: SourceProvidersListBranchesRequest,
    opts?: OperationOpts
  ): Observable<Array<string> | AjaxResponse<Array<string>>> {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'sourceProvidersListBranches'
    );
    throwIfNullOrUndefined(project, 'project', 'sourceProvidersListBranches');
    throwIfNullOrUndefined(
      providerName,
      'providerName',
      'sourceProvidersListBranches'
    );
    throwIfNullOrUndefined(
      apiVersion,
      'apiVersion',
      'sourceProvidersListBranches'
    );

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

    if (serviceEndpointId != null) {
      query['serviceEndpointId'] = serviceEndpointId;
    }
    if (repository != null) {
      query['repository'] = repository;
    }
    if (branchName != null) {
      query['branchName'] = branchName;
    }

    return this.request<Array<string>>(
      {
        url: '/{organization}/{project}/_apis/sourceProviders/{providerName}/branches'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{providerName}', encodeURI(providerName)),
        method: 'GET',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Gets a list of source code repositories.
   */
  sourceProvidersListRepositories({
    organization,
    project,
    providerName,
    apiVersion,
    serviceEndpointId,
    repository,
    resultSet,
    pageResults,
    continuationToken
  }: SourceProvidersListRepositoriesRequest): Observable<SourceRepositories>;
  sourceProvidersListRepositories(
    {
      organization,
      project,
      providerName,
      apiVersion,
      serviceEndpointId,
      repository,
      resultSet,
      pageResults,
      continuationToken
    }: SourceProvidersListRepositoriesRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<SourceRepositories>>;
  sourceProvidersListRepositories(
    {
      organization,
      project,
      providerName,
      apiVersion,
      serviceEndpointId,
      repository,
      resultSet,
      pageResults,
      continuationToken
    }: SourceProvidersListRepositoriesRequest,
    opts?: OperationOpts
  ): Observable<SourceRepositories | AjaxResponse<SourceRepositories>> {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'sourceProvidersListRepositories'
    );
    throwIfNullOrUndefined(
      project,
      'project',
      'sourceProvidersListRepositories'
    );
    throwIfNullOrUndefined(
      providerName,
      'providerName',
      'sourceProvidersListRepositories'
    );
    throwIfNullOrUndefined(
      apiVersion,
      'apiVersion',
      'sourceProvidersListRepositories'
    );

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

    if (serviceEndpointId != null) {
      query['serviceEndpointId'] = serviceEndpointId;
    }
    if (repository != null) {
      query['repository'] = repository;
    }
    if (resultSet != null) {
      query['resultSet'] = resultSet;
    }
    if (pageResults != null) {
      query['pageResults'] = pageResults;
    }
    if (continuationToken != null) {
      query['continuationToken'] = continuationToken;
    }

    return this.request<SourceRepositories>(
      {
        url: '/{organization}/{project}/_apis/sourceProviders/{providerName}/repositories'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{providerName}', encodeURI(providerName)),
        method: 'GET',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Gets a list of webhooks installed in the given source code repository.
   */
  sourceProvidersListWebhooks({
    organization,
    project,
    providerName,
    apiVersion,
    serviceEndpointId,
    repository
  }: SourceProvidersListWebhooksRequest): Observable<Array<RepositoryWebhook>>;
  sourceProvidersListWebhooks(
    {
      organization,
      project,
      providerName,
      apiVersion,
      serviceEndpointId,
      repository
    }: SourceProvidersListWebhooksRequest,
    opts?: OperationOpts
  ): Observable<AjaxResponse<Array<RepositoryWebhook>>>;
  sourceProvidersListWebhooks(
    {
      organization,
      project,
      providerName,
      apiVersion,
      serviceEndpointId,
      repository
    }: SourceProvidersListWebhooksRequest,
    opts?: OperationOpts
  ): Observable<
    Array<RepositoryWebhook> | AjaxResponse<Array<RepositoryWebhook>>
  > {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'sourceProvidersListWebhooks'
    );
    throwIfNullOrUndefined(project, 'project', 'sourceProvidersListWebhooks');
    throwIfNullOrUndefined(
      providerName,
      'providerName',
      'sourceProvidersListWebhooks'
    );
    throwIfNullOrUndefined(
      apiVersion,
      'apiVersion',
      'sourceProvidersListWebhooks'
    );

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

    if (serviceEndpointId != null) {
      query['serviceEndpointId'] = serviceEndpointId;
    }
    if (repository != null) {
      query['repository'] = repository;
    }

    return this.request<Array<RepositoryWebhook>>(
      {
        url: '/{organization}/{project}/_apis/sourceProviders/{providerName}/webhooks'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{providerName}', encodeURI(providerName)),
        method: 'GET',
        headers,
        query
      },
      opts?.responseOpts
    );
  }

  /**
   * Recreates the webhooks for the specified triggers in the given source code repository.
   */
  sourceProvidersRestoreWebhooks({
    organization,
    project,
    providerName,
    apiVersion,
    body,
    serviceEndpointId,
    repository
  }: SourceProvidersRestoreWebhooksRequest): Observable<void>;
  sourceProvidersRestoreWebhooks(
    {
      organization,
      project,
      providerName,
      apiVersion,
      body,
      serviceEndpointId,
      repository
    }: SourceProvidersRestoreWebhooksRequest,
    opts?: OperationOpts
  ): Observable<void | AjaxResponse<void>>;
  sourceProvidersRestoreWebhooks(
    {
      organization,
      project,
      providerName,
      apiVersion,
      body,
      serviceEndpointId,
      repository
    }: SourceProvidersRestoreWebhooksRequest,
    opts?: OperationOpts
  ): Observable<void | AjaxResponse<void>> {
    throwIfNullOrUndefined(
      organization,
      'organization',
      'sourceProvidersRestoreWebhooks'
    );
    throwIfNullOrUndefined(
      project,
      'project',
      'sourceProvidersRestoreWebhooks'
    );
    throwIfNullOrUndefined(
      providerName,
      'providerName',
      'sourceProvidersRestoreWebhooks'
    );
    throwIfNullOrUndefined(
      apiVersion,
      'apiVersion',
      'sourceProvidersRestoreWebhooks'
    );
    throwIfNullOrUndefined(body, 'body', 'sourceProvidersRestoreWebhooks');

    const headers: HttpHeaders = {
      'Content-Type': 'application/json',
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

    if (serviceEndpointId != null) {
      query['serviceEndpointId'] = serviceEndpointId;
    }
    if (repository != null) {
      query['repository'] = repository;
    }

    return this.request<void>(
      {
        url: '/{organization}/{project}/_apis/sourceProviders/{providerName}/webhooks'
          .replace('{organization}', encodeURI(organization))
          .replace('{project}', encodeURI(project))
          .replace('{providerName}', encodeURI(providerName)),
        method: 'POST',
        headers,
        query,
        body: body
      },
      opts?.responseOpts
    );
  }
}

/**
 * @export
 * @enum {string}
 */
export enum SourceProvidersListRepositoriesResultSetEnum {
  All = 'all',
  Top = 'top'
}
