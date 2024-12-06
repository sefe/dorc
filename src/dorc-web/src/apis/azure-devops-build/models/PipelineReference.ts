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

import type { JobReference, PhaseReference, StageReference } from './index';

/**
 * Pipeline reference
 * @export
 * @interface PipelineReference
 */
export interface PipelineReference {
  /**
   * @type {JobReference}
   * @memberof PipelineReference
   */
  jobReference?: JobReference;
  /**
   * @type {PhaseReference}
   * @memberof PipelineReference
   */
  phaseReference?: PhaseReference;
  /**
   * Reference of the pipeline with which this pipeline instance is related.
   * @type {number}
   * @memberof PipelineReference
   */
  pipelineId?: number;
  /**
   * @type {StageReference}
   * @memberof PipelineReference
   */
  stageReference?: StageReference;
}
