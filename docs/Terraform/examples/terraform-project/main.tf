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
# Module source lives in https://github.com/sefe/dorc-terraform-modules and is
# pulled at use time. In DOrc, use the Stock Modules page's "Deploy from
# template" wizard (POST /api/Terraform/templates/
# {name}/{version}/instantiate) instead - it creates a Terraform component
# with TerraformSourceType = Catalog and TerraformTemplateName = "cosmosdb",
# TerraformTemplateVersion = "1.0.0" (see docs/Terraform/MODULES.md).
module "cosmosdb" {
  source = "git::https://github.com/sefe/dorc-terraform-modules.git//stock-modules/cosmosdb?ref=stock-modules/cosmosdb/v1.0.0"

  resource_group_name = var.resource_group_name
  location            = var.location
  account_name        = var.cosmosdb_account_name
  database_name       = var.database_name

  tags = var.tags
}
