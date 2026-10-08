variable "resource_group_name" {
  description = "Name of the resource group the Service Bus namespace is created in."
  type        = string
}

variable "location" {
  description = "Azure region for the Service Bus namespace."
  type        = string
}

variable "namespace_name" {
  description = "Globally unique Service Bus namespace name (6-50 chars, letters, numbers and hyphens, starting with a letter, ending with a letter or number)."
  type        = string

  validation {
    condition     = can(regex("^[a-zA-Z][a-zA-Z0-9-]{4,48}[a-zA-Z0-9]$", var.namespace_name))
    error_message = "namespace_name must be 6-50 characters of letters, numbers and hyphens, starting with a letter and ending with a letter or number."
  }
}

variable "sku" {
  description = "Namespace SKU. Private endpoints and VNet integration require Premium."
  type        = string
  default     = "Standard"

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.sku)
    error_message = "sku must be one of Basic, Standard or Premium."
  }
}

variable "queue_name" {
  description = "Name of the queue to create in the namespace (1-260 chars; letters, numbers, periods, hyphens, underscores and slashes; starts and ends with a letter or number)."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9]([A-Za-z0-9._/-]{0,258}[A-Za-z0-9])?$", var.queue_name))
    error_message = "queue_name must be 1-260 characters of letters, numbers, periods, hyphens, underscores and slashes, starting and ending with a letter or number."
  }
}

variable "max_delivery_count" {
  description = "Maximum number of delivery attempts before a message is dead-lettered."
  type        = number
  default     = 10

  validation {
    condition     = var.max_delivery_count >= 1 && var.max_delivery_count <= 2000
    error_message = "max_delivery_count must be between 1 and 2000."
  }
}

variable "enable_partitioning" {
  description = "Enable partitioning across multiple message brokers (Basic/Standard only; set at creation time)."
  type        = bool
  default     = false
}

variable "public_network_access_enabled" {
  description = "Whether the namespace accepts traffic from public networks. Disabling requires the Premium SKU with private endpoints."
  type        = bool
  default     = true
}

variable "local_auth_enabled" {
  description = "Whether SAS (local) authentication is enabled. Disabled by default (Entra ID only); set true to allow SAS."
  type        = bool
  default     = false
}

variable "tags" {
  description = "Tags applied to the Service Bus namespace."
  type        = map(string)
  default     = {}
}
