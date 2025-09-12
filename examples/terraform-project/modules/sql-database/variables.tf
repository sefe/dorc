variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
}

variable "location" {
  description = "Azure region for resources"
  type        = string
}

variable "database_name" {
  description = "Name of the SQL database"
  type        = string
}

variable "server_name" {
  description = "Name of the SQL server"
  type        = string
}

variable "environment" {
  description = "Environment name"
  type        = string
}

variable "admin_password" {
  description = "Administrator password for SQL server"
  type        = string
  sensitive   = true
  default     = "P@ssw0rd123!" # Should be overridden with secure value
}

variable "max_size_gb" {
  description = "Maximum size of the database in GB"
  type        = number
  default     = 10
}

variable "sku_name" {
  description = "SKU name for the database"
  type        = string
  default     = "Basic"
}

variable "tags" {
  description = "Tags to apply to resources"
  type        = map(string)
  default     = {}
}