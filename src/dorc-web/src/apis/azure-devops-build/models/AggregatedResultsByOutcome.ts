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
 * @interface AggregatedResultsByOutcome
 */
export interface AggregatedResultsByOutcome {
  /**
   * @type {number}
   * @memberof AggregatedResultsByOutcome
   */
  count?: number;
  /**
   * @type {string}
   * @memberof AggregatedResultsByOutcome
   */
  duration?: string;
  /**
   * @type {string}
   * @memberof AggregatedResultsByOutcome
   */
  groupByField?: string;
  /**
   * @type {object}
   * @memberof AggregatedResultsByOutcome
   */
  groupByValue?: object;
  /**
   * @type {string}
   * @memberof AggregatedResultsByOutcome
   */
  outcome?: AggregatedResultsByOutcomeOutcomeEnum;
  /**
   * @type {number}
   * @memberof AggregatedResultsByOutcome
   */
  rerunResultCount?: number;
}

/**
 * @export
 * @enum {string}
 */
export enum AggregatedResultsByOutcomeOutcomeEnum {
  Unspecified = 'unspecified',
  None = 'none',
  Passed = 'passed',
  Failed = 'failed',
  Inconclusive = 'inconclusive',
  Timeout = 'timeout',
  Aborted = 'aborted',
  Blocked = 'blocked',
  NotExecuted = 'notExecuted',
  Warning = 'warning',
  Error = 'error',
  NotApplicable = 'notApplicable',
  Paused = 'paused',
  InProgress = 'inProgress',
  NotImpacted = 'notImpacted'
}
