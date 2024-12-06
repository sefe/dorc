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
 * Represents metadata about builds in the system.
 * @export
 * @interface BuildMetric
 */
export interface BuildMetric {
  /**
   * The date for the scope.
   * @type {string}
   * @memberof BuildMetric
   */
  date?: string;
  /**
   * The value.
   * @type {number}
   * @memberof BuildMetric
   */
  intValue?: number;
  /**
   * The name of the metric.
   * @type {string}
   * @memberof BuildMetric
   */
  name?: string;
  /**
   * The scope.
   * @type {string}
   * @memberof BuildMetric
   */
  scope?: string;
}
