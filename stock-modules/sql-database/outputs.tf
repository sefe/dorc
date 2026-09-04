output "sql_server_id" {
  description = "Resource ID of the logical SQL server. Required by downstream modules (firewall rules, vnet rules, AAD admin)."
  value       = azurerm_mssql_server.this.id
}

output "sql_server_name" {
  description = "Name of the logical SQL server."
  value       = azurerm_mssql_server.this.name
}

output "sql_server_fqdn" {
  description = "Fully-qualified DNS name of the SQL server (e.g. my-server.database.windows.net)."
  value       = azurerm_mssql_server.this.fully_qualified_domain_name
}

output "database_id" {
  description = "Resource ID of the database."
  value       = azurerm_mssql_database.this.id
}

output "database_name" {
  description = "Name of the database."
  value       = azurerm_mssql_database.this.name
}
