resource "azurerm_mssql_server" "sql" {
  name                         = "sql-${local.resource_prefix}"
  resource_group_name          = azurerm_resource_group.rg.name
  location                     = azurerm_resource_group.rg.location
  version                      = "12.0"
  administrator_login          = var.sql_admin_login
  administrator_login_password = var.sql_admin_password
  minimum_tls_version          = "1.2"
  tags                         = local.tags
}

resource "azurerm_mssql_database" "db" {
  name        = "dorc-${var.environment}"
  server_id   = azurerm_mssql_server.sql.id
  collation   = "SQL_Latin1_General_CP1_CI_AS"
  sku_name    = "GP_S_Gen5_1"
  max_size_gb = 32

  auto_pause_delay_in_minutes = 60
  min_capacity                = 0.5

  tags = local.tags
}

# Allow Azure services to access SQL Server
resource "azurerm_mssql_firewall_rule" "allow_azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.sql.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# Allow ACA subnet via VNet rule
resource "azurerm_mssql_virtual_network_rule" "aca" {
  name      = "aca-subnet"
  server_id = azurerm_mssql_server.sql.id
  subnet_id = azurerm_subnet.sql.id
}
