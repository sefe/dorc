// tslint:disable
/**
 * Dorc.Api
 * No description provided (generated by Openapi Generator https://github.com/openapitools/openapi-generator)
 *
 * The version of the OpenAPI document: 1.0
 * 
 *
 * NOTE: This class is auto generated by OpenAPI Generator (https://openapi-generator.tech).
 * https://openapi-generator.tech
 * Do not edit the class manually.
 */

import type {
    EnvironmentApiModel,
    ProjectApiModel,
} from './';

/**
 * @export
 * @interface EnvironmentApiModelTemplateApiModel
 */
export interface EnvironmentApiModelTemplateApiModel {
    /**
     * @type {ProjectApiModel}
     * @memberof EnvironmentApiModelTemplateApiModel
     */
    Project?: ProjectApiModel;
    /**
     * @type {Array<EnvironmentApiModel>}
     * @memberof EnvironmentApiModelTemplateApiModel
     */
    Items?: Array<EnvironmentApiModel> | null;
}
