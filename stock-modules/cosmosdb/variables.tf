variable "resource_group_name" {
  description = "Name of the resource group the Cosmos DB account is created in."
  type        = string
}

variable "location" {
  description = "Azure region for the Cosmos DB account."
  type        = string
}

variable "account_name" {
  description = "Globally unique Cosmos DB account name (3-44 chars, lowercase letters, numbers and hyphens)."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9-]{1,42}[a-z0-9]$", var.account_name))
    error_message = "account_name must be 3-44 characters of lowercase letters, numbers and hyphens, starting and ending with a letter or number."
  }
}

variable "database_name" {
  description = "Name of the SQL (Core) API database to create in the account."
  type        = string

  validation {
    condition     = can(regex("^[^/\\\\#?]{1,255}$", var.database_name))
    error_message = "database_name must be 1-255 characters and must not contain '/', '\\', '#' or '?'."
  }
}

variable "consistency_level" {
  description = "Default consistency level of the account. For BoundedStaleness the module pins max_interval_in_seconds=300 and max_staleness_prefix=100000."
  type        = string
  default     = "Session"

  validation {
    condition     = contains(["Eventual", "ConsistentPrefix", "Session", "BoundedStaleness", "Strong"], var.consistency_level)
    error_message = "consistency_level must be one of Eventual, ConsistentPrefix, Session, BoundedStaleness or Strong."
  }
}

variable "throughput" {
  description = "Provisioned throughput (RU/s) for the database. Must be a multiple of 100 between 400 and 100000."
  type        = number
  default     = 400

  validation {
    condition     = var.throughput >= 400 && var.throughput <= 100000 && var.throughput % 100 == 0
    error_message = "throughput must be a multiple of 100 between 400 and 100000."
  }
}

variable "public_network_access_enabled" {
  description = "Whether the account accepts traffic from public networks. Secure by default; opt in explicitly."
  type        = bool
  default     = false
}

variable "local_authentication_disabled" {
  description = "Disable account key (local) authentication so only Entra ID data-plane auth is accepted."
  type        = bool
  default     = false
}

variable "tags" {
  description = "Tags applied to the Cosmos DB account."
  type        = map(string)
  default     = {}
}
