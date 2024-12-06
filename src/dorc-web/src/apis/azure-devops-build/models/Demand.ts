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
 * Represents a demand used by a definition or build.
 * @export
 * @interface Demand
 */
export interface Demand {
  /**
   * The name of the capability referenced by the demand.
   * @type {string}
   * @memberof Demand
   */
  name?: string;
  /**
   * The demanded value.
   * @type {string}
   * @memberof Demand
   */
  value?: string;
}
