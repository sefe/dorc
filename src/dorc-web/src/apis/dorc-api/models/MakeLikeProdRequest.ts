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
    RequestProperty,
} from './';

/**
 * @export
 * @interface MakeLikeProdRequest
 */
export interface MakeLikeProdRequest {
    /**
     * @type {string}
     * @memberof MakeLikeProdRequest
     */
    TargetEnv?: string | null;
    /**
     * @type {string}
     * @memberof MakeLikeProdRequest
     */
    DataBackup?: string | null;
    /**
     * @type {string}
     * @memberof MakeLikeProdRequest
     */
    BundleName?: string | null;
    /**
     * @type {Array<RequestProperty>}
     * @memberof MakeLikeProdRequest
     */
    BundleProperties?: Array<RequestProperty> | null;
}
