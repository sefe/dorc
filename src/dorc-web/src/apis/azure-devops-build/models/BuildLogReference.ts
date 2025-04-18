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
 * Represents a reference to a build log.
 * @export
 * @interface BuildLogReference
 */
export interface BuildLogReference {
  /**
   * The ID of the log.
   * @type {number}
   * @memberof BuildLogReference
   */
  id?: number;
  /**
   * The type of the log location.
   * @type {string}
   * @memberof BuildLogReference
   */
  type?: string;
  /**
   * A full link to the log resource.
   * @type {string}
   * @memberof BuildLogReference
   */
  url?: string;
}
