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
    DeploymentRequestApiModel,
} from './';

/**
 * @export
 * @interface GetRequestStatusesListResponseDto
 */
export interface GetRequestStatusesListResponseDto {
    /**
     * @type {number}
     * @memberof GetRequestStatusesListResponseDto
     */
    CurrentPage?: number;
    /**
     * @type {number}
     * @memberof GetRequestStatusesListResponseDto
     */
    TotalItems?: number;
    /**
     * @type {number}
     * @memberof GetRequestStatusesListResponseDto
     */
    TotalPages?: number;
    /**
     * @type {Array<DeploymentRequestApiModel>}
     * @memberof GetRequestStatusesListResponseDto
     */
    Items?: Array<DeploymentRequestApiModel> | null;
}
