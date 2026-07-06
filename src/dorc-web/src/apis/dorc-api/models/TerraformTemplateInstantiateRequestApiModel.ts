// Hand-written extension matching Dorc.ApiModel.TerraformTemplateInstantiateRequestApiModel.

export interface TerraformTemplateInstantiateRequestApiModel {
    ProjectId: number;
    ComponentName?: string | null;
    ParentComponentId?: number | null;
    // present switches the endpoint to create-and-deploy mode.
    EnvironmentName?: string | null;
    Parameters?: { [key: string]: string };
}
