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
 * @interface ComponentApiModel
 */
export interface ComponentApiModel {
    /**
     * @type {number}
     * @memberof ComponentApiModel
     */
    ComponentId?: number | null;
    /**
     * @type {string}
     * @memberof ComponentApiModel
     */
    ComponentName?: string | null;
    /**
     * @type {string}
     * @memberof ComponentApiModel
     */
    ScriptPath?: string | null;
    /**
     * @type {boolean}
     * @memberof ComponentApiModel
     */
    NonProdOnly?: boolean;
    /**
     * @type {boolean}
     * @memberof ComponentApiModel
     */
    StopOnFailure?: boolean;
    /**
     * @type {number}
     * @memberof ComponentApiModel
     */
    ParentId?: number;
    /**
     * @type {boolean}
     * @memberof ComponentApiModel
     */
    IsEnabled?: boolean | null;
}
