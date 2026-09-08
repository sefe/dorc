variable "resource_group_name" {
  description = "Name of an existing resource group to deploy the virtual network into."
  type        = string

  validation {
    condition     = length(var.resource_group_name) > 0 && length(var.resource_group_name) <= 90
    error_message = "resource_group_name must be 1-90 characters."
  }
}

variable "location" {
  description = "Azure region (e.g. westeurope, eastus2). Must match the resource group."
  type        = string

  validation {
    condition     = length(var.location) > 0
    error_message = "location is required."
  }
}

variable "vnet_name" {
  description = "Name of the virtual network. Lowercase recommended; must be unique within the resource group."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,62}[A-Za-z0-9]$", var.vnet_name))
    error_message = "vnet_name must be 2-64 chars, alphanumeric plus . _ -, and start/end alphanumeric."
  }
}

variable "address_space" {
  description = "CIDR address space(s) for the virtual network. At least one CIDR is required."
  type        = list(string)

  validation {
    condition     = length(var.address_space) > 0
    error_message = "address_space must contain at least one CIDR block."
  }

  validation {
    condition     = alltrue([for c in var.address_space : can(cidrnetmask(c))])
    error_message = "every entry in address_space must be a valid CIDR (e.g. 10.0.0.0/16)."
  }
}

variable "subnets" {
  description = "Subnets to create inside the vnet. Each subnet has a name and an address_prefix CIDR."
  type = list(object({
    name           = string
    address_prefix = string
  }))
  default = []

  validation {
    condition     = alltrue([for s in var.subnets : can(cidrnetmask(s.address_prefix))])
    error_message = "every subnets[].address_prefix must be a valid CIDR."
  }

  validation {
    condition     = length(distinct([for s in var.subnets : s.name])) == length(var.subnets)
    error_message = "subnet names must be unique within the vnet."
  }
}

variable "tags" {
  description = "Tags applied to every resource created by this module."
  type        = map(string)
  default     = {}
}
