variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
  default     = ""
}

variable "location" {
  description = "Azure region for resources"
  type        = string
  default     = "East US"
}

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
  default     = "dev"
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