output "sql_server_fqdn" {
  description = "Fully qualified domain name of the SQL server"
  value       = azurerm_mssql_server.main.fully_qualified_domain_name
}

output "database_id" {
  description = "ID of the SQL database"
  value       = azurerm_mssql_database.main.id
}

output "database_name" {
  description = "Name of the SQL database"
  value       = azurerm_mssql_database.main.name
}