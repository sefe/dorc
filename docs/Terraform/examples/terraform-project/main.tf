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
#module "sql_managed_instance" {
#  count  = var.enable_sql_mi ? 1 : 0
#  source = "./modules/sql-managed-instance"
#  
#  resource_group_name = var.resource_group_name
#  location           = var.location
#  instance_name      = var.sql_mi_name
#  environment        = var.environment
#  
#  tags = var.tags
#}