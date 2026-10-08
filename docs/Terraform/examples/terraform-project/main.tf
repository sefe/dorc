terraform {
  required_version = ">= 1.5.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.100"
    }
  }

  # No backend block here - DOrc renders a managed Azure Blob backend
  # at deploy time (see docs/Terraform/STATE-MODEL.md). User-checked-in
  # backend blocks are rejected at pre-flight.
}

provider "azurerm" {
  features {}
}

# Reference a stock module from the DOrc stock-modules library at a pinned tag.
# In CI/local you can use a relative source; in DOrc, use the Stock Modules
# page's "Deploy from template" wizard (POST /api/Terraform/templates/
# {name}/{version}/instantiate) instead - it creates a Terraform component
# with TerraformSourceType = Catalog and TerraformTemplateName = "cosmosdb",
# TerraformTemplateVersion = "1.0.0" (see docs/Terraform/MODULES.md).
module "cosmosdb" {
  source = "../../../../stock-modules/cosmosdb"

  resource_group_name = var.resource_group_name
  location            = var.location
  account_name        = var.cosmosdb_account_name
  database_name       = var.database_name

  tags = var.tags
}
