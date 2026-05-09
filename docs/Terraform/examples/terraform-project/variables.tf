variable "resource_group_name" {
  description = "Existing resource group."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "sql_server_name" {
  description = "SQL logical server name."
  type        = string
}

variable "database_name" {
  description = "SQL database name."
  type        = string
}

variable "sql_admin_password" {
  description = "Supplied at deploy time by a DOrc sensitive property; never committed to source. Min 16 chars."
  type        = string
  sensitive   = true
}

variable "tags" {
  description = "Tags applied to deployed resources."
  type        = map(string)
  default     = {}
}
