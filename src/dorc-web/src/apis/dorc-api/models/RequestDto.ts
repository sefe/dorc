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
 * @interface RequestDto
 */
export interface RequestDto {
    /**
     * @type {string}
     * @memberof RequestDto
     */
    Project?: string | null;
    /**
     * @type {string}
     * @memberof RequestDto
     */
    Environment?: string | null;
    /**
     * @type {string}
     * @memberof RequestDto
     */
    BuildUrl?: string | null;
    /**
     * @type {string}
     * @memberof RequestDto
     */
    BuildText?: string | null;
    /**
     * @type {string}
     * @memberof RequestDto
     */
    BuildNum?: string | null;
    /**
     * @type {Array<string>}
     * @memberof RequestDto
     */
    Components?: Array<string> | null;
    /**
     * @type {boolean}
     * @memberof RequestDto
     */
    Pinned?: boolean | null;
    /**
     * @type {Array<RequestProperty>}
     * @memberof RequestDto
     */
    RequestProperties?: Array<RequestProperty> | null;
    /**
     * @type {string}
     * @memberof RequestDto
     */
    VstsUrl?: string | null;
}
