# Example Terraform Project for DOrc

This directory contains a complete example of a Terraform project configured to work with DOrc's Terraform Runner.

## Project Structure

```
terraform-project/
├── main.tf                    # Main Terraform configuration
├── variables.tf               # Variable definitions
├── environments/              # Environment-specific configurations
│   ├── dev/
│   │   └── terraform.tfvars
│   ├── staging/
│   │   └── terraform.tfvars
│   └── prod/
│       └── terraform.tfvars
└── modules/                   # Reusable Terraform modules
    ├── sql-database/
    │   ├── main.tf
    │   ├── variables.tf
    │   └── outputs.tf
    └── sql-managed-instance/
        ├── main.tf
        ├── variables.tf
        └── outputs.tf
```

## How to Use with DOrc

You can use this example project with any of the supported Terraform source types in DOrc.

### SharedFolder source

1. **Upload Files**: Place this `terraform-project` directory under your ScriptRoot (for example, `C:\DOrc\Scripts\0001-Terraform\terraform-project`).
2. **Create Component**:
    - Component Name: "SQL Database Infrastructure"
    - Component Type: "Terraform"
    - Terraform Source Type: `SharedFolder`
    - Script Path: `C:\DOrc\Scripts\0001-Terraform`
    - Terraform Sub Path: `terraform-project`
3. **Configure Properties**: Set up environment-specific properties in DOrc that match the variables in `variables.tf`.
4. **Deploy**: Create deployment requests through DOrc UI.

### Git source

1. **Repository**: Store this `terraform-project` directory in a Git repository (for example, under the repo root or in a subfolder).
2. **Project Settings**: Set **Terraform Git Repository URL** on the project to your repo URL. Two Git sources are supported: Azure and Github. They differ by authentication mechanism, PAT is used for Github, and Azure app reg is used for Azure (it's already incorporated in DOrc).
3. **Create Component**:
    - Component Name: "SQL Database Infrastructure (Git)"
    - Component Type: "Terraform"
    - Terraform Source Type: `Git`
    - Terraform Git Branch: `main` (or another branch)
    - Terraform Sub Path: `terraform-project` (or the relative path inside the repo)
4. **Configure Properties**: Set up environment-specific properties in DOrc that match the variables in `variables.tf`. Set up `Terraform_Git_PAT` environment variable and place there PAT token for Git (if Github is used). For Azure Git Dorc already has credentials.
5. **Deploy**: Create deployment requests; DOrc clones the repo and runs Terraform from the specified subpath in component.

### AzureArtifact source

1. **CI Pipeline**: Configure an Azure DevOps pipeline that produces an artifact containing this `terraform-project` structure.
2. **Project Settings**: Existing **Artefacts Url** and **Artefacts Sub Paths** in Project configuration will be used, meaning the choosen on Deploy page build will be consumed.
3. **Create Component**:
    - Component Name: "SQL Database Infrastructure (AzureArtifact)"
    - Component Type: "Terraform"
    - Terraform Source Type: `AzureArtifact`
    - Terraform Sub Path: `terraform-project`
4. **Configure Properties**: Same as above.
5. **Deploy**: Create deployment requests; DOrc downloads the artifact and runs Terraform from the specified build and component subpath.

## Key Features

- **Environment Separation**: Different configurations for dev/staging/prod
- **Modular Design**: Reusable modules for SQL Database and Managed Instance
- **Variable-Driven**: All environment-specific values are parameterized
- **DOrc Integration**: Designed to work seamlessly with DOrc's property system and all supported Terraform source types (SharedFolder, Git, AzureArtifact)

See the parent Terraform documentation (`terraform-setup-example.md` and `terraform-source-configuration.md`) for detailed setup instructions.