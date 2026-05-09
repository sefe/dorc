variable "resource_group_name" {
  description = "Name of an existing resource group."
  type        = string
}

variable "location" {
  description = "Azure region; must match the resource group."
  type        = string
}

variable "account_name" {
  description = "Storage account name. 3-24 chars, lowercase alphanumeric only (Azure constraint)."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{3,24}$", var.account_name))
    error_message = "account_name must be 3-24 lowercase alphanumeric characters (no hyphens, no uppercase)."
  }
}

variable "account_tier" {
  description = "Performance tier."
  type        = string
  default     = "Standard"

  validation {
    condition     = contains(["Standard", "Premium"], var.account_tier)
    error_message = "account_tier must be Standard or Premium."
  }
}

variable "replication_type" {
  description = "Replication strategy. LRS = locally redundant, ZRS = zone redundant, GRS/RAGRS = geo redundant."
  type        = string
  default     = "LRS"

  validation {
    condition     = contains(["LRS", "ZRS", "GRS", "RAGRS", "GZRS", "RAGZRS"], var.replication_type)
    error_message = "replication_type must be one of: LRS, ZRS, GRS, RAGRS, GZRS, RAGZRS."
  }
}

variable "min_tls_version" {
  description = "Minimum TLS version. Defaults to TLS1_2; older values are rejected by validation."
  type        = string
  default     = "TLS1_2"

  validation {
    condition     = contains(["TLS1_2"], var.min_tls_version)
    error_message = "min_tls_version must be TLS1_2 (older versions are not permitted)."
  }
}

variable "public_network_access_enabled" {
  description = "If true, the storage account is reachable from the public network. Disabled by default; explicit opt-in."
  type        = bool
  default     = false
}

variable "tags" {
  description = "Tags applied to every resource created by this module."
  type        = map(string)
  default     = {}
}
