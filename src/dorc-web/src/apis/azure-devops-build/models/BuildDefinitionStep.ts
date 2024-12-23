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

import type { TaskDefinitionReference } from './index';

/**
 * Represents a step in a build phase.
 * @export
 * @interface BuildDefinitionStep
 */
export interface BuildDefinitionStep {
  /**
   * Indicates whether this step should run even if a previous step fails.
   * @type {boolean}
   * @memberof BuildDefinitionStep
   */
  alwaysRun?: boolean;
  /**
   * A condition that determines whether this step should run.
   * @type {string}
   * @memberof BuildDefinitionStep
   */
  condition?: string;
  /**
   * Indicates whether the phase should continue even if this step fails.
   * @type {boolean}
   * @memberof BuildDefinitionStep
   */
  continueOnError?: boolean;
  /**
   * The display name for this step.
   * @type {string}
   * @memberof BuildDefinitionStep
   */
  displayName?: string;
  /**
   * Indicates whether the step is enabled.
   * @type {boolean}
   * @memberof BuildDefinitionStep
   */
  enabled?: boolean;
  /**
   * @type {{ [key: string]: string; }}
   * @memberof BuildDefinitionStep
   */
  environment?: { [key: string]: string };
  /**
   * @type {{ [key: string]: string; }}
   * @memberof BuildDefinitionStep
   */
  inputs?: { [key: string]: string };
  /**
   * The reference name for this step.
   * @type {string}
   * @memberof BuildDefinitionStep
   */
  refName?: string;
  /**
   * Number of retries.
   * @type {number}
   * @memberof BuildDefinitionStep
   */
  retryCountOnTaskFailure?: number;
  /**
   * @type {TaskDefinitionReference}
   * @memberof BuildDefinitionStep
   */
  task?: TaskDefinitionReference;
  /**
   * The time, in minutes, that this step is allowed to run.
   * @type {number}
   * @memberof BuildDefinitionStep
   */
  timeoutInMinutes?: number;
}
