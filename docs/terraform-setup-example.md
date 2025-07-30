# Setting Up a Terraform Project with DOrc

This guide shows you how to set up a project to deploy Terraform infrastructure using DOrc's Terraform Runner functionality.

## Overview

DOrc's Terraform Runner provides a complete workflow for deploying Terraform infrastructure with approval controls:

1. **Plan Creation**: Terraform plans are generated automatically
2. **Review Process**: Plans require manual approval before execution
3. **Execution**: Approved plans are executed with full logging
4. **Monitoring**: Status tracking throughout the deployment process

## Project Structure

Create your Terraform project with the following structure:

```
my-terraform-project/
├── environments/
│   ├── dev/
│   │   └── terraform.tfvars
│   ├── staging/
│   │   └── terraform.tfvars
│   └── prod/
│       └── terraform.tfvars
├── modules/
│   ├── sql-database/
│   │   ├── main.tf
│   │   ├── variables.tf
│   │   └── outputs.tf
│   └── sql-managed-instance/
│       ├── main.tf
│       ├── variables.tf
│       └── outputs.tf
├── main.tf
├── variables.tf
├── outputs.tf
└── providers.tf
```

## Example Configuration Files

### 1. Main Terraform Configuration (`main.tf`)

```hcl
terraform {
  required_version = ">= 1.0"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }
}

provider "azurerm" {
  features {}
}

# SQL Database module example
module "sql_database" {
  count  = var.enable_sql_database ? 1 : 0
  source = "./modules/sql-database"
  
  resource_group_name = var.resource_group_name
  location           = var.location
  database_name      = var.database_name
  server_name        = var.sql_server_name
  environment        = var.environment
  
  tags = var.tags
}

# SQL Managed Instance module example
module "sql_managed_instance" {
  count  = var.enable_sql_mi ? 1 : 0
  source = "./modules/sql-managed-instance"
  
  resource_group_name = var.resource_group_name
  location           = var.location
  instance_name      = var.sql_mi_name
  environment        = var.environment
  
  tags = var.tags
}
```

### 2. Variables Definition (`variables.tf`)

```hcl
variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
}

variable "location" {
  description = "Azure region for resources"
  type        = string
  default     = "East US"
}

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
}

variable "enable_sql_database" {
  description = "Enable SQL Database deployment"
  type        = bool
  default     = false
}

variable "enable_sql_mi" {
  description = "Enable SQL Managed Instance deployment"
  type        = bool
  default     = false
}

variable "database_name" {
  description = "Name of the SQL database"
  type        = string
  default     = ""
}

variable "sql_server_name" {
  description = "Name of the SQL server"
  type        = string
  default     = ""
}

variable "sql_mi_name" {
  description = "Name of the SQL Managed Instance"
  type        = string
  default     = ""
}

variable "tags" {
  description = "Tags to apply to resources"
  type        = map(string)
  default     = {}
}
```

### 3. Environment-Specific Variables (`environments/dev/terraform.tfvars`)

```hcl
# Development environment configuration
resource_group_name = "rg-myapp-dev"
location           = "East US"
environment        = "dev"

# Enable SQL Database for dev
enable_sql_database = true
database_name      = "myapp-dev-db"
sql_server_name    = "myapp-dev-sql"

# Disable SQL MI for dev (cost optimization)
enable_sql_mi      = false

tags = {
  Environment = "dev"
  Project     = "MyApp"
  Owner       = "DevTeam"
}
```

### 4. SQL Database Module (`modules/sql-database/main.tf`)

```hcl
resource "azurerm_mssql_server" "main" {
  name                         = var.server_name
  resource_group_name          = var.resource_group_name
  location                     = var.location
  version                      = "12.0"
  administrator_login          = "sqladmin"
  administrator_login_password = var.admin_password
  minimum_tls_version          = "1.2"

  tags = var.tags
}

resource "azurerm_mssql_database" "main" {
  name           = var.database_name
  server_id      = azurerm_mssql_server.main.id
  collation      = "SQL_Latin1_General_CP1_CI_AS"
  license_type   = "LicenseIncluded"
  max_size_gb    = var.max_size_gb
  sku_name       = var.sku_name

  tags = var.tags
}

resource "azurerm_mssql_firewall_rule" "main" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.main.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}
```

## DOrc Configuration

### 1. Create Project in DOrc

1. Log into DOrc web interface
2. Navigate to **Projects** section
3. Click **Add New Project**
4. Fill in project details:
   - **Name**: "MyApp Terraform Infrastructure"
   - **Description**: "Terraform infrastructure for MyApp"

### 2. Create Components

Create components for each Terraform module:

#### Component 1: SQL Database Infrastructure
- **Component Name**: "SQL Database Infrastructure"
- **Component Type**: **Terraform** (Important!)
- **Script Path**: Path to your Terraform files (e.g., `/path/to/terraform/sql-database`)
- **Stop on Failure**: Yes
- **Enabled**: Yes

#### Component 2: SQL Managed Instance Infrastructure  
- **Component Name**: "SQL MI Infrastructure"
- **Component Type**: **Terraform** (Important!)
- **Script Path**: Path to your Terraform files (e.g., `/path/to/terraform/sql-mi`)
- **Stop on Failure**: Yes
- **Enabled**: Yes

### 3. Set Up Environments

Configure environments in DOrc:

- **Development**: `dev`
- **Staging**: `staging` 
- **Production**: `prod`

### 4. Configure Properties

Set up environment-specific properties that DOrc will pass to Terraform:

#### Environment Properties (Development)
- `resource_group_name` = "rg-myapp-dev"
- `location` = "East US"
- `environment` = "dev"
- `enable_sql_database` = "true"
- `enable_sql_mi` = "false"
- `database_name` = "myapp-dev-db"
- `sql_server_name` = "myapp-dev-sql"

#### Environment Properties (Production)
- `resource_group_name` = "rg-myapp-prod"
- `location` = "East US"
- `environment` = "prod"
- `enable_sql_database` = "true"
- `enable_sql_mi` = "true"
- `database_name` = "myapp-prod-db"
- `sql_server_name` = "myapp-prod-sql"
- `sql_mi_name` = "myapp-prod-mi"

## Deployment Workflow

### 1. Create Deployment Request

1. Navigate to **Deployments** in DOrc
2. Click **New Deployment Request**
3. Select your project: "MyApp Terraform Infrastructure"
4. Choose target environment: "dev", "staging", or "prod"
5. Select components to deploy
6. Submit the request

### 2. Review Terraform Plan

Once the deployment request is processed:

1. DOrc will generate Terraform plans for selected components
2. Deployment status will show **"Waiting Confirmation"**
3. Click the **"View Plan"** button in the deployment results grid
4. Review the Terraform plan in the dialog:
   - **Resources to be created** (+ green)
   - **Resources to be modified** (~ yellow)  
   - **Resources to be destroyed** (- red)

### 3. Approve or Decline

After reviewing the plan:
- Click **"Confirm"** to approve and execute the plan
- Click **"Decline"** to cancel the deployment

### 4. Monitor Execution

Once approved:
- Status changes to **"Confirmed"** then **"Running"**
- Monitor progress in real-time via the deployment logs
- Final status will be **"Success"** or **"Failed"**

## File Structure in DOrc

Place your Terraform files in the configured script paths:

```
/dorc-scripts/terraform/
├── sql-database/
│   ├── main.tf
│   ├── variables.tf
│   ├── outputs.tf
│   └── environments/
│       ├── dev.tfvars
│       ├── staging.tfvars
│       └── prod.tfvars
└── sql-mi/
    ├── main.tf
    ├── variables.tf
    ├── outputs.tf
    └── environments/
        ├── dev.tfvars
        ├── staging.tfvars
        └── prod.tfvars
```

## Property Mapping

DOrc properties are automatically converted to Terraform variables:

| DOrc Property | Terraform Variable | Example Value |
|---------------|-------------------|---------------|
| `resource_group_name` | `TF_VAR_resource_group_name` | "rg-myapp-dev" |
| `location` | `TF_VAR_location` | "East US" |
| `environment` | `TF_VAR_environment` | "dev" |
| `database_name` | `TF_VAR_database_name` | "myapp-dev-db" |

## Security Considerations

- **Approval Process**: All Terraform plans require manual approval
- **Environment Permissions**: Users need appropriate environment permissions
- **State Management**: Terraform state is managed automatically
- **Sensitive Values**: Use DOrc's secure property features for secrets

## Complete Project Reference Data Example

Here's a complete example of the JSON format for project reference data with Terraform components:

```json
{
  "Project": {
    "ProjectId": 230,
    "ProjectName": "Terraform Testing",
    "ProjectDescription": "Terraform infrastructure project",
    "ArtefactsUrl": "https://dev.azure.com/sefe/",
    "ArtefactsSubPaths": "Deployment Orchestrator",
    "ArtefactsBuildRegex": "",
    "SourceDatabase": null
  },
  "Components": [{
    "ComponentName": "SQL Database Infrastructure",
    "ScriptPath": "0001-Terraform\\terraform-project",
    "NonProdOnly": false,
    "StopOnFailure": false,
    "ParentId": 0,
    "IsEnabled": true,
    "ComponentType": "Terraform",
    "Children": []
  }]
}
```

### Important Notes:

1. **ComponentType**: Must be the string `"Terraform"` (case-sensitive)
2. **ScriptPath**: Must be a simple string path, not a JSON object
3. **Path Separators**: Use double backslashes `\\` for Windows paths or forward slashes `/` for Unix paths

## Troubleshooting

### Common Issues

1. **Component Type not set to Terraform**
   - Ensure `ComponentType` is set to `"Terraform"` in component configuration
   - PowerShell components will not trigger the Terraform workflow

2. **Invalid JSON format**
   - ScriptPath must be a string, not an object: `"ScriptPath": "path/to/terraform"`
   - ComponentType must be a string: `"ComponentType": "Terraform"`

3. **Plan generation fails**
   - Check Terraform file syntax
   - Verify all required variables are defined
   - Ensure Terraform is installed on DOrc servers

4. **Permission errors**
   - Verify user has deployment permissions for target environment
   - Check Azure credentials configuration

5. **State lock errors**
   - Ensure no other Terraform operations are running
   - Check Azure storage account accessibility

## Next Steps

- Set up automated testing for Terraform configurations
- Implement state file backup strategies  
- Configure monitoring and alerting for infrastructure changes
- Set up GitOps integration for Terraform code updates

## API Integration

You can also manage Terraform deployments programmatically:

```bash
# Get Terraform plan
curl -X GET "https://your-dorc-api/terraform/plan/{deploymentResultId}" \
  -H "Authorization: Bearer your-token"

# Confirm plan
curl -X POST "https://your-dorc-api/terraform/plan/{deploymentResultId}/confirm" \
  -H "Authorization: Bearer your-token"

# Decline plan  
curl -X POST "https://your-dorc-api/terraform/plan/{deploymentResultId}/decline" \
  -H "Authorization: Bearer your-token"
```

This setup provides a complete infrastructure-as-code workflow with proper approval controls and monitoring capabilities.