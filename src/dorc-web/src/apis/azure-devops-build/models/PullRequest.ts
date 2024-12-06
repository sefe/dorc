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

import type { IdentityRef, ReferenceLinks } from './index';

/**
 * Represents a pull request object.  These are retrieved from Source Providers.
 * @export
 * @interface PullRequest
 */
export interface PullRequest {
  /**
   * @type {ReferenceLinks}
   * @memberof PullRequest
   */
  _links?: ReferenceLinks;
  /**
   * @type {IdentityRef}
   * @memberof PullRequest
   */
  author?: IdentityRef;
  /**
   * Current state of the pull request, e.g. open, merged, closed, conflicts, etc.
   * @type {string}
   * @memberof PullRequest
   */
  currentState?: string;
  /**
   * Description for the pull request.
   * @type {string}
   * @memberof PullRequest
   */
  description?: string;
  /**
   * Returns if pull request is draft
   * @type {boolean}
   * @memberof PullRequest
   */
  draft?: boolean;
  /**
   * Unique identifier for the pull request
   * @type {string}
   * @memberof PullRequest
   */
  id?: string;
  /**
   * The name of the provider this pull request is associated with.
   * @type {string}
   * @memberof PullRequest
   */
  providerName?: string;
  /**
   * Source branch ref of this pull request
   * @type {string}
   * @memberof PullRequest
   */
  sourceBranchRef?: string;
  /**
   * Owner of the source repository of this pull request
   * @type {string}
   * @memberof PullRequest
   */
  sourceRepositoryOwner?: string;
  /**
   * Target branch ref of this pull request
   * @type {string}
   * @memberof PullRequest
   */
  targetBranchRef?: string;
  /**
   * Owner of the target repository of this pull request
   * @type {string}
   * @memberof PullRequest
   */
  targetRepositoryOwner?: string;
  /**
   * Title of the pull request.
   * @type {string}
   * @memberof PullRequest
   */
  title?: string;
}
