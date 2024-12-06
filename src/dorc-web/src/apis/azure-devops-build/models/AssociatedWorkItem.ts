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
 * @interface AssociatedWorkItem
 */
export interface AssociatedWorkItem {
  /**
   * @type {string}
   * @memberof AssociatedWorkItem
   */
  assignedTo?: string;
  /**
   * Id of associated the work item.
   * @type {number}
   * @memberof AssociatedWorkItem
   */
  id?: number;
  /**
   * @type {string}
   * @memberof AssociatedWorkItem
   */
  state?: string;
  /**
   * @type {string}
   * @memberof AssociatedWorkItem
   */
  title?: string;
  /**
   * REST Url of the work item.
   * @type {string}
   * @memberof AssociatedWorkItem
   */
  url?: string;
  /**
   * @type {string}
   * @memberof AssociatedWorkItem
   */
  webUrl?: string;
  /**
   * @type {string}
   * @memberof AssociatedWorkItem
   */
  workItemType?: string;
}
