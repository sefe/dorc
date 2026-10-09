variable "project" {
  description = "Aiven console project the Kafka service lives in."
  type        = string
  default     = "trading-traveler"

  validation {
    condition     = can(regex("^[a-z0-9-]+$", var.project))
    error_message = "project must be lowercase letters, numbers and hyphens."
  }
}

variable "service_name" {
  description = "SEFE traveler Kafka instance (Aiven BYOC) to create the topic on. Leave empty to select automatically from environment_tier: pr -> traveler-production, ut/qa/pp -> traveler-non-prod, dv -> traveler-unstable-dev. The service is referenced, never managed, by this module."
  type        = string
  default     = ""

  validation {
    condition     = contains(["", "traveler-production", "traveler-non-prod", "traveler-unstable-dev"], var.service_name)
    error_message = "service_name must be one of the SEFE traveler Kafka instances (traveler-production, traveler-non-prod, traveler-unstable-dev) or empty to derive it from environment_tier."
  }
}

# --- Topic name segments (SEFE Kafka messaging standard) -------------------
# <business_vertical>.<environment_tier>.<scope>.<data_grouping>.<data_description>.<integrity_level>[.<origin>][.<publisher_identifier>]

variable "business_vertical" {
  description = "Two-character business vertical code, e.g. 'tr' for Trading."
  type        = string

  validation {
    condition     = can(regex("^[a-z]{2}$", var.business_vertical))
    error_message = "business_vertical must be exactly two lowercase letters."
  }
}

variable "environment_tier" {
  description = "Environment tier the topic belongs to."
  type        = string

  validation {
    condition     = contains(["dv", "ut", "qa", "pp", "pr"], var.environment_tier)
    error_message = "environment_tier must be one of dv, ut, qa, pp, pr."
  }
}

variable "scope" {
  description = "Topic scope: gbl (global), lcl (local), usr (user), tst (test)."
  type        = string

  validation {
    condition     = contains(["gbl", "lcl", "usr", "tst"], var.scope)
    error_message = "scope must be one of gbl, lcl, usr, tst."
  }
}

variable "data_grouping" {
  description = "Logical data grouping segment of the topic name, e.g. an application or domain name."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9_-]+$", var.data_grouping))
    error_message = "data_grouping must be lowercase letters, numbers, underscores and hyphens."
  }
}

variable "data_description" {
  description = "Data description segment of the topic name, e.g. the message/entity type."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9_-]+$", var.data_description))
    error_message = "data_description must be lowercase letters, numbers, underscores and hyphens."
  }
}

variable "integrity_level" {
  description = "Data integrity level classification (il0-il3)."
  type        = string

  validation {
    condition     = contains(["il0", "il1", "il2", "il3"], var.integrity_level)
    error_message = "integrity_level must be one of il0, il1, il2, il3."
  }
}

variable "origin" {
  description = "Optional three-character origin system code. Leave empty to omit the segment."
  type        = string
  default     = ""

  validation {
    condition     = var.origin == "" || can(regex("^[a-z]{3}$", var.origin))
    error_message = "origin must be empty or exactly three lowercase letters."
  }
}

variable "publisher_identifier" {
  description = "Optional publisher identifier segment. Leave empty to omit the segment."
  type        = string
  default     = ""

  validation {
    condition     = var.publisher_identifier == "" || can(regex("^[a-z0-9_-]+$", var.publisher_identifier))
    error_message = "publisher_identifier must be empty or lowercase letters, numbers, underscores and hyphens."
  }
}

# --- Topic settings ---------------------------------------------------------

variable "partitions" {
  description = "Number of partitions. Can be increased later but never decreased."
  type        = number
  default     = 3

  validation {
    condition     = var.partitions >= 1 && floor(var.partitions) == var.partitions
    error_message = "partitions must be a whole number of at least 1."
  }
}

variable "replication" {
  description = "Replication factor. Aiven requires at least 2."
  type        = number
  default     = 2

  validation {
    condition     = var.replication >= 2 && floor(var.replication) == var.replication
    error_message = "replication must be a whole number of at least 2."
  }
}

variable "cleanup_policy" {
  description = "Log cleanup policy for the topic."
  type        = string
  default     = "delete"

  validation {
    condition     = contains(["delete", "compact", "compact,delete"], var.cleanup_policy)
    error_message = "cleanup_policy must be one of delete, compact, or compact,delete."
  }
}

variable "min_insync_replicas" {
  description = "Minimum number of in-sync replicas required to acknowledge a write."
  type        = number
  default     = 2

  validation {
    condition     = var.min_insync_replicas >= 1 && floor(var.min_insync_replicas) == var.min_insync_replicas
    error_message = "min_insync_replicas must be a whole number of at least 1."
  }
}

variable "retention_ms" {
  description = "Message retention in milliseconds; -1 for unlimited. Default is 7 days."
  type        = number
  default     = 604800000

  validation {
    condition     = var.retention_ms >= -1
    error_message = "retention_ms must be -1 (unlimited) or a non-negative number."
  }
}

variable "retention_bytes" {
  description = "Maximum size a partition can grow to before old segments are discarded; -1 for unlimited."
  type        = number
  default     = -1

  validation {
    condition     = var.retention_bytes >= -1
    error_message = "retention_bytes must be -1 (unlimited) or a non-negative number."
  }
}

variable "termination_protection" {
  description = "Client-side deletion protection preventing Terraform from destroying the topic. Secure by default; set false only for throwaway environments."
  type        = bool
  default     = true
}
