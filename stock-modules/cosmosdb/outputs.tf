output "cosmosdb_account_id" {
  description = "Resource ID of the Cosmos DB account."
  value       = azurerm_cosmosdb_account.this.id
}

output "cosmosdb_account_name" {
  description = "Name of the Cosmos DB account."
  value       = azurerm_cosmosdb_account.this.name
}

output "cosmosdb_endpoint" {
  description = "Document endpoint of the Cosmos DB account. No account keys are output; consumers should use Entra ID auth or a data block."
  value       = azurerm_cosmosdb_account.this.endpoint
}

output "database_id" {
  description = "Resource ID of the SQL database."
  value       = azurerm_cosmosdb_sql_database.this.id
}

output "database_name" {
  description = "Name of the SQL database."
  value       = azurerm_cosmosdb_sql_database.this.name
}
