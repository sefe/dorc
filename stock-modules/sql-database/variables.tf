variable "resource_group_name" {
  description = "Name of an existing resource group."
  type        = string

  validation {
    condition     = length(var.resource_group_name) > 0 && length(var.resource_group_name) <= 90
    error_message = "resource_group_name must be 1-90 characters (Azure constraint)."
  }
}

variable "location" {
  description = "Azure region; must match the resource group."
  type        = string

  validation {
    condition     = length(var.location) > 0
    error_message = "location is required."
  }
}

variable "server_name" {
  description = "SQL logical server name. 1-63 chars, lowercase alphanumeric + hyphens; cannot start or end with a hyphen."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", var.server_name))
    error_message = "server_name must be 1-63 lowercase alphanumeric chars or hyphens, not starting/ending with a hyphen."
  }
}

variable "database_name" {
  description = "SQL database name. 1-128 chars; cannot contain reserved characters (<>*%&:\\/?)."
  type        = string

  validation {
    condition     = can(regex("^[^<>*%&:\\\\/?]{1,128}$", var.database_name))
    error_message = "database_name must be 1-128 chars and not contain <>*%&:\\/?."
  }
}

variable "administrator_login" {
  description = "SQL admin login. Must not be a reserved name (sa, admin, root, etc.). Defaulted to 'sqladmin'."
  type        = string
  default     = "sqladmin"

  validation {
    condition = !contains(
      ["admin", "administrator", "sa", "root", "dbmanager", "loginmanager", "dbo", "guest", "public"],
      lower(var.administrator_login)
    )
    error_message = "administrator_login must not be a reserved SQL Server name."
  }
}

variable "administrator_password" {
  description = "SQL admin password. Required and never defaulted: must be supplied via a secret-aware DOrc property at deploy time. Treated as sensitive."
  type        = string
  sensitive   = true

  validation {
    condition     = length(var.administrator_password) >= 16
    error_message = "administrator_password must be at least 16 characters."
  }
}

variable "sku_name" {
  description = "Database SKU. Allow-listed to prevent accidental selection of expensive tiers."
  type        = string
  default     = "S0"

  validation {
    condition     = contains(["Basic", "S0", "S1", "S2", "S3", "GP_S_Gen5_2", "GP_Gen5_2", "GP_Gen5_4"], var.sku_name)
    error_message = "sku_name must be one of: Basic, S0, S1, S2, S3, GP_S_Gen5_2, GP_Gen5_2, GP_Gen5_4."
  }
}

variable "max_size_gb" {
  description = "Maximum database size in GB."
  type        = number
  default     = 10

  validation {
    condition     = var.max_size_gb >= 1 && var.max_size_gb <= 4096
    error_message = "max_size_gb must be between 1 and 4096."
  }
}

variable "allow_azure_services" {
  description = "If true, creates a firewall rule permitting Azure-internal services. Disabled by default; explicit opt-in."
  type        = bool
  default     = false
}

variable "tags" {
  description = "Tags applied to every resource created by this module."
  type        = map(string)
  default     = {}
}
