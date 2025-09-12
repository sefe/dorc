// TypeScript interface for TerraformPlanApiModel
// This is manually created until swagger generation is updated

/**
 * @export
 * @interface TerraformPlanApiModel
 */
export interface TerraformPlanApiModel {
    /**
     * @type {number}
     * @memberof TerraformPlanApiModel
     */
    DeploymentResultId?: number;
    /**
     * @type {string}
     * @memberof TerraformPlanApiModel
     */
    PlanContent?: string | null;
    /**
     * @type {string}
     * @memberof TerraformPlanApiModel
     */
    BlobUrl?: string | null;
    /**
     * @type {Date}
     * @memberof TerraformPlanApiModel
     */
    CreatedAt?: Date;
    /**
     * @type {string}
     * @memberof TerraformPlanApiModel
     */
    Status?: string | null;
}

/**
 * @export
 * @interface TerraformConfirmResponse
 */
export interface TerraformConfirmResponse {
    /**
     * @type {string}
     * @memberof TerraformConfirmResponse
     */
    message?: string | null;
    /**
     * @type {number}
     * @memberof TerraformConfirmResponse
     */
    deploymentResultId?: number;
    /**
     * @type {string}
     * @memberof TerraformConfirmResponse
     */
    confirmedBy?: string | null;
    /**
     * @type {Date}
     * @memberof TerraformConfirmResponse
     */
    confirmedAt?: Date;
}

/**
 * @export
 * @interface TerraformDeclineResponse
 */
export interface TerraformDeclineResponse {
    /**
     * @type {string}
     * @memberof TerraformDeclineResponse
     */
    message?: string | null;
    /**
     * @type {number}
     * @memberof TerraformDeclineResponse
     */
    deploymentResultId?: number;
    /**
     * @type {string}
     * @memberof TerraformDeclineResponse
     */
    declinedBy?: string | null;
    /**
     * @type {Date}
     * @memberof TerraformDeclineResponse
     */
    declinedAt?: Date;
}