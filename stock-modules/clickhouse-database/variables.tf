variable "project" {
  description = "Aiven console project the ClickHouse service lives in."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9-]+$", var.project))
    error_message = "project must be lowercase letters, numbers and hyphens."
  }
}

variable "service_name" {
  description = "Name of the existing Aiven ClickHouse service the database is created in. The service is referenced, never managed, by this module."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9-]+$", var.service_name))
    error_message = "service_name must be lowercase letters, numbers and hyphens."
  }
}

variable "database_name" {
  description = "Name of the ClickHouse database to create. Cannot be changed after creation (forces recreation)."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9_-]{1,255}$", var.database_name))
    error_message = "database_name must be 1-255 characters of letters, numbers, underscores and hyphens."
  }
}

variable "termination_protection" {
  description = "Client-side deletion protection preventing Terraform from destroying the database. Secure by default; set false only for throwaway environments."
  type        = bool
  default     = true
}
