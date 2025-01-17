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
 * Stage in pipeline
 * @export
 * @interface StageReference
 */
export interface StageReference {
  /**
   * Attempt number of stage
   * @type {number}
   * @memberof StageReference
   */
  attempt?: number;
  /**
   * Name of the stage. Maximum supported length for name is 256 character.
   * @type {string}
   * @memberof StageReference
   */
  stageName?: string;
}
