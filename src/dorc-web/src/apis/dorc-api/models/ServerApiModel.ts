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

/**
 * @export
 * @interface ServerApiModel
 */
export interface ServerApiModel {
    /**
     * @type {Array<string>}
     * @memberof ServerApiModel
     */
    EnvironmentNames?: Array<string> | null;
    /**
     * @type {boolean}
     * @memberof ServerApiModel
     */
    UserEditable?: boolean;
    /**
     * @type {number}
     * @memberof ServerApiModel
     */
    ServerId?: number;
    /**
     * @type {string}
     * @memberof ServerApiModel
     */
    Name?: string | null;
    /**
     * @type {string}
     * @memberof ServerApiModel
     */
    OsName?: string | null;
    /**
     * @type {string}
     * @memberof ServerApiModel
     */
    ApplicationTags?: string | null;
}
