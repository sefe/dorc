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
# In CI/local you can use a relative source; in DOrc deploys, set the
# component property Terraform_Template_Name = "sql-database" and
# Terraform_Template_Version = "1.0.0" (catalog API; see docs/Terraform/MODULE-CONTRACT.md).
module "sql" {
  source = "../../../../stock-modules/sql-database"

  resource_group_name    = var.resource_group_name
  location               = var.location
  server_name            = var.sql_server_name
  database_name          = var.database_name
  administrator_password = var.sql_admin_password

  tags = var.tags
}
