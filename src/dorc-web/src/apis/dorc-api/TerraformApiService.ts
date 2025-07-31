// Terraform API service for making HTTP calls to the Terraform endpoints
// This is manually created until swagger generation is updated

import { TerraformPlanApiModel, TerraformConfirmResponse, TerraformDeclineResponse } from './models/index';

export class TerraformApiService {
    private baseUrl: string;

    constructor(baseUrl: string = '') {
        this.baseUrl = baseUrl;
    }

    /**
     * Gets Terraform plan for a deployment result
     * @param deploymentResultId The deployment result ID
     * @returns Promise<TerraformPlanApiModel>
     */
    async getTerraformPlan(deploymentResultId: number): Promise<TerraformPlanApiModel> {
        const url = `${this.baseUrl}/terraform/plan/${deploymentResultId}`;
        
        const response = await fetch(url, {
            method: 'GET',
            headers: {
                'Accept': 'application/json',
            },
            credentials: 'include'
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Failed to get Terraform plan: ${response.status} ${response.statusText} - ${errorText}`);
        }

        const data = await response.json();
        return data as TerraformPlanApiModel;
    }

    /**
     * Confirms a Terraform plan for execution
     * @param deploymentResultId The deployment result ID
     * @returns Promise<TerraformConfirmResponse>
     */
    async confirmTerraformPlan(deploymentResultId: number): Promise<TerraformConfirmResponse> {
        const url = `${this.baseUrl}/terraform/plan/${deploymentResultId}/confirm`;
        
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
            },
            credentials: 'include'
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Failed to confirm Terraform plan: ${response.status} ${response.statusText} - ${errorText}`);
        }

        const data = await response.json();
        return data as TerraformConfirmResponse;
    }

    /**
     * Declines a Terraform plan
     * @param deploymentResultId The deployment result ID
     * @returns Promise<TerraformDeclineResponse>
     */
    async declineTerraformPlan(deploymentResultId: number): Promise<TerraformDeclineResponse> {
        const url = `${this.baseUrl}/terraform/plan/${deploymentResultId}/decline`;
        
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
            },
            credentials: 'include'
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Failed to decline Terraform plan: ${response.status} ${response.statusText} - ${errorText}`);
        }

        const data = await response.json();
        return data as TerraformDeclineResponse;
    }
}