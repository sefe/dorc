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
 * An update to the retention parameters of a retention lease.
 * @export
 * @interface RetentionLeaseUpdate
 */
export interface RetentionLeaseUpdate {
  /**
   * The number of days to consider the lease valid. A retention lease valid for more than 100 years (36500 days) will display as retaining the build \"forever\".
   * @type {number}
   * @memberof RetentionLeaseUpdate
   */
  daysValid?: number;
  /**
   * If set, this lease will also prevent the pipeline from being deleted while the lease is still valid.
   * @type {boolean}
   * @memberof RetentionLeaseUpdate
   */
  protectPipeline?: boolean;
}
