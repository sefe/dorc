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
    EnvironmentDetailsApiModel,
} from './';

/**
 * @export
 * @interface EnvironmentApiModel
 */
export interface EnvironmentApiModel {
    /**
     * @type {number}
     * @memberof EnvironmentApiModel
     */
    EnvironmentId?: number;
    /**
     * @type {string}
     * @memberof EnvironmentApiModel
     */
    EnvironmentName?: string | null;
    /**
     * @type {boolean}
     * @memberof EnvironmentApiModel
     */
    EnvironmentSecure?: boolean;
    /**
     * @type {boolean}
     * @memberof EnvironmentApiModel
     */
    EnvironmentIsProd?: boolean;
    /**
     * @type {boolean}
     * @memberof EnvironmentApiModel
     */
    UserEditable?: boolean;
    /**
     * @type {boolean}
     * @memberof EnvironmentApiModel
     */
    IsOwner?: boolean;
    /**
     * @type {number}
     * @memberof EnvironmentApiModel
     */
    ParentId?: number | null;
    /**
     * @type {boolean}
     * @memberof EnvironmentApiModel
     */
    IsParent?: boolean;
    /**
     * @type {EnvironmentDetailsApiModel}
     * @memberof EnvironmentApiModel
     */
    Details?: EnvironmentDetailsApiModel;
    /**
     * @type {EnvironmentApiModel}
     * @memberof EnvironmentApiModel
     */
    ParentEnvironment?: EnvironmentApiModel;
    /**
     * @type {Array<EnvironmentApiModel>}
     * @memberof EnvironmentApiModel
     */
    ChildEnvironments?: Array<EnvironmentApiModel> | null;
}
