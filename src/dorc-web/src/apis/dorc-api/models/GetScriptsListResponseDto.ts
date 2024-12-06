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
    ScriptApiModel,
} from './';

/**
 * @export
 * @interface GetScriptsListResponseDto
 */
export interface GetScriptsListResponseDto {
    /**
     * @type {number}
     * @memberof GetScriptsListResponseDto
     */
    CurrentPage?: number;
    /**
     * @type {number}
     * @memberof GetScriptsListResponseDto
     */
    TotalItems?: number;
    /**
     * @type {number}
     * @memberof GetScriptsListResponseDto
     */
    TotalPages?: number;
    /**
     * @type {Array<ScriptApiModel>}
     * @memberof GetScriptsListResponseDto
     */
    Items?: Array<ScriptApiModel> | null;
}
