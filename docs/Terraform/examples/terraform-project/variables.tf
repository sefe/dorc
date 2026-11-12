variable "resource_group_name" {
  description = "Existing resource group."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "cosmosdb_account_name" {
  description = "Cosmos DB account name."
  type        = string
}

variable "database_name" {
  description = "Cosmos DB SQL database name."
  type        = string
}

variable "tags" {
  description = "Tags applied to deployed resources."
  type        = map(string)
  default     = {}
}
