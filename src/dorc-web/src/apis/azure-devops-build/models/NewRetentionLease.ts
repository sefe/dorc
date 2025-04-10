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

/**
 * Required information to create a new retention lease.
 * @export
 * @interface NewRetentionLease
 */
export interface NewRetentionLease {
  /**
   * The number of days to consider the lease valid. A retention lease valid for more than 100 years (36500 days) will display as retaining the build \"forever\".
   * @type {number}
   * @memberof NewRetentionLease
   */
  daysValid?: number;
  /**
   * The pipeline definition of the run.
   * @type {number}
   * @memberof NewRetentionLease
   */
  definitionId?: number;
  /**
   * User-provided string that identifies the owner of a retention lease.
   * @type {string}
   * @memberof NewRetentionLease
   */
  ownerId?: string;
  /**
   * If set, this lease will also prevent the pipeline from being deleted while the lease is still valid.
   * @type {boolean}
   * @memberof NewRetentionLease
   */
  protectPipeline?: boolean;
  /**
   * The pipeline run to protect.
   * @type {number}
   * @memberof NewRetentionLease
   */
  runId?: number;
}
