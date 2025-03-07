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
 *
 * @export
 * @interface BuildsDeletedEvent1
 */
export interface BuildsDeletedEvent1 {
  /**
   * @type {Array<number>}
   * @memberof BuildsDeletedEvent1
   */
  buildIds?: Array<number>;
  /**
   * The ID of the definition.
   * @type {number}
   * @memberof BuildsDeletedEvent1
   */
  definitionId?: number;
  /**
   * The ID of the project.
   * @type {string}
   * @memberof BuildsDeletedEvent1
   */
  projectId?: string;
}
