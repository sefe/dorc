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
 * @interface DeployComponentDto
 */
export interface DeployComponentDto {
    /**
     * @type {number}
     * @memberof DeployComponentDto
     */
    Id?: number;
    /**
     * @type {string}
     * @memberof DeployComponentDto
     */
    Name?: string | null;
    /**
     * @type {string}
     * @memberof DeployComponentDto
     */
    Description?: string | null;
    /**
     * @type {number}
     * @memberof DeployComponentDto
     */
    ParentId?: number | null;
    /**
     * @type {number}
     * @memberof DeployComponentDto
     */
    NumOfChildren?: number | null;
    /**
     * @type {boolean}
     * @memberof DeployComponentDto
     */
    IsEnabled?: boolean | null;
}
