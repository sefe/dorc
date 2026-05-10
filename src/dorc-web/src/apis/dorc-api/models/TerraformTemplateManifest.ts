// Hand-written extension matching Dorc.Terraform.Catalog.TerraformTemplateManifest.
// When the openapi codegen runs, this file should be regenerated and the hand-
// written shape kept in sync with the C# record.

export interface TerraformTemplateSource {
    Kind: string;
    Locator: string;
    Ref: string;
    SubPath?: string | null;
}

export type TerraformParameterType = 'String' | 'Number' | 'Bool' | 'List' | 'Map' | 'Object';

export interface TerraformTemplateParameter {
    Name: string;
    Type: TerraformParameterType;
    Required: boolean;
    Description?: string | null;
    Default?: string | null;
    AllowedValues?: string[] | null;
    Pattern?: string | null;
    Min?: number | null;
    Max?: number | null;
    Sensitive: boolean;
}

export interface TerraformTemplateOutput {
    Name: string;
    Type: TerraformParameterType;
    Description?: string | null;
    Sensitive: boolean;
}

export interface TerraformTemplateManifest {
    Name: string;
    Version: string;
    Source: TerraformTemplateSource;
    Parameters: TerraformTemplateParameter[];
    Outputs: TerraformTemplateOutput[];
    Description?: string | null;
    Tags: string[];
    Category?: string | null;
    RequiredProviders: { [key: string]: string };
    RequiredTerraformVersion: string;
    Owner?: string | null;
    Deprecated: boolean;
    DeprecationReason?: string | null;
}
