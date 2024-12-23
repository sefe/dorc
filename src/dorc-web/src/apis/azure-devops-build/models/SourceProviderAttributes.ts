// tslint:disable
/**
 * Build
 * No description provided (generated by Openapi Generator https://github.com/openapitools/openapi-generator)
 *
 * The version of the OpenAPI document: 6.1-preview
 * Contact: nugetvss@microsoft.com
 *
 * NOTE: This class is auto generated by OpenAPI Generator (https://openapi-generator.tech).
 * https://openapi-generator.tech
 * Do not edit the class manually.
 */

import type { SupportedTrigger } from './index';

/**
 *
 * @export
 * @interface SourceProviderAttributes
 */
export interface SourceProviderAttributes {
  /**
   * The name of the source provider.
   * @type {string}
   * @memberof SourceProviderAttributes
   */
  name?: string;
  /**
   * The capabilities supported by this source provider.
   * @type {{ [key: string]: boolean; }}
   * @memberof SourceProviderAttributes
   */
  supportedCapabilities?: { [key: string]: boolean };
  /**
   * The types of triggers supported by this source provider.
   * @type {Array<SupportedTrigger>}
   * @memberof SourceProviderAttributes
   */
  supportedTriggers?: Array<SupportedTrigger>;
}
