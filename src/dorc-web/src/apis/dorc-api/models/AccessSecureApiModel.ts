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
    AccessControlApiModel,
    AccessControlType,
} from './';

/**
 * @export
 * @interface AccessSecureApiModel
 */
export interface AccessSecureApiModel {
    /**
     * @type {AccessControlType}
     * @memberof AccessSecureApiModel
     */
    Type?: AccessControlType;
    /**
     * @type {string}
     * @memberof AccessSecureApiModel
     */
    Name?: string | null;
    /**
     * @type {string}
     * @memberof AccessSecureApiModel
     */
    ObjectId?: string;
    /**
     * @type {boolean}
     * @memberof AccessSecureApiModel
     */
    UserEditable?: boolean;
    /**
     * @type {Array<AccessControlApiModel>}
     * @memberof AccessSecureApiModel
     */
    Privileges?: Array<AccessControlApiModel> | null;
}


