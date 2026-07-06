// tslint:disable
/**
 * Dorc.Api
 * Hand-written model for the Terraform template instantiate endpoint
 * (the checked-in swagger predates it). Mirrors the anonymous envelope
 * returned by TerraformController.InstantiateTemplate in
 * create-and-deploy mode; in create-only mode the endpoint returns a
 * bare ComponentApiModel instead, hence the union alias below.
 */

import type { ComponentApiModel } from './ComponentApiModel';

/**
 * @export
 * @interface TerraformTemplateInstantiateResponseApiModel
 */
export interface TerraformTemplateInstantiateResponseApiModel {
    /**
     * The Catalog-mode component that was created.
     * @type {ComponentApiModel}
     * @memberof TerraformTemplateInstantiateResponseApiModel
     */
    component?: ComponentApiModel;
    /**
     * Id of the deploy request submitted for the new component.
     * @type {number}
     * @memberof TerraformTemplateInstantiateResponseApiModel
     */
    requestId?: number;
    /**
     * Status text of the submitted deploy request.
     * @type {string}
     * @memberof TerraformTemplateInstantiateResponseApiModel
     */
    requestStatus?: string;
}

/**
 * Create-and-deploy mode returns the envelope; create-only mode returns
 * the bare component.
 * @export
 */
export type TerraformTemplateInstantiateResult =
    | TerraformTemplateInstantiateResponseApiModel
    | ComponentApiModel;
