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
    DatabaseApiModel,
} from './';

/**
 * @export
 * @interface GetDatabaseApiModelListResponseDto
 */
export interface GetDatabaseApiModelListResponseDto {
    /**
     * @type {number}
     * @memberof GetDatabaseApiModelListResponseDto
     */
    CurrentPage?: number;
    /**
     * @type {number}
     * @memberof GetDatabaseApiModelListResponseDto
     */
    TotalItems?: number;
    /**
     * @type {number}
     * @memberof GetDatabaseApiModelListResponseDto
     */
    TotalPages?: number;
    /**
     * @type {Array<DatabaseApiModel>}
     * @memberof GetDatabaseApiModelListResponseDto
     */
    Items?: Array<DatabaseApiModel> | null;
}
