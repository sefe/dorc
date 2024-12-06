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
 * Represents information about resources used by builds in the system.
 * @export
 * @interface BuildResourceUsage
 */
export interface BuildResourceUsage {
  /**
   * The number of build agents.
   * @type {number}
   * @memberof BuildResourceUsage
   */
  distributedTaskAgents?: number;
  /**
   * The number of paid private agent slots.
   * @type {number}
   * @memberof BuildResourceUsage
   */
  paidPrivateAgentSlots?: number;
  /**
   * The total usage.
   * @type {number}
   * @memberof BuildResourceUsage
   */
  totalUsage?: number;
  /**
   * The number of XAML controllers.
   * @type {number}
   * @memberof BuildResourceUsage
   */
  xamlControllers?: number;
}
