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
    EnvironmentContentBuildsApiModel,
    EnvironmentContentWindowsServicesApiModel,
    ProjectApiModel,
    ServerApiModel,
    UserApiModel,
} from './';

/**
 * @export
 * @interface EnvironmentContentApiModel
 */
export interface EnvironmentContentApiModel {
    /**
     * @type {string}
     * @memberof EnvironmentContentApiModel
     */
    EnvironmentName?: string | null;
    /**
     * @type {string}
     * @memberof EnvironmentContentApiModel
     */
    FileShare?: string | null;
    /**
     * @type {string}
     * @memberof EnvironmentContentApiModel
     */
    Description?: string | null;
    /**
     * @type {Array<DatabaseApiModel>}
     * @memberof EnvironmentContentApiModel
     */
    DbServers?: Array<DatabaseApiModel> | null;
    /**
     * @type {Array<ServerApiModel>}
     * @memberof EnvironmentContentApiModel
     */
    AppServers?: Array<ServerApiModel> | null;
    /**
     * @type {Array<EnvironmentContentWindowsServicesApiModel>}
     * @memberof EnvironmentContentApiModel
     */
    WindowsServices?: Array<EnvironmentContentWindowsServicesApiModel> | null;
    /**
     * @type {Array<EnvironmentContentBuildsApiModel>}
     * @memberof EnvironmentContentApiModel
     */
    Builds?: Array<EnvironmentContentBuildsApiModel> | null;
    /**
     * @type {Array<UserApiModel>}
     * @memberof EnvironmentContentApiModel
     */
    EndurUsers?: Array<UserApiModel> | null;
    /**
     * @type {Array<UserApiModel>}
     * @memberof EnvironmentContentApiModel
     */
    DelegatedUsers?: Array<UserApiModel> | null;
    /**
     * @type {Array<ProjectApiModel>}
     * @memberof EnvironmentContentApiModel
     */
    MappedProjects?: Array<ProjectApiModel> | null;
}
