resource "azurerm_log_analytics_workspace" "logs" {
  name                = "log-${local.resource_prefix}"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.tags
}

resource "azurerm_container_app_environment" "env" {
  name                       = "cae-${local.resource_prefix}"
  location                   = azurerm_resource_group.rg.location
  resource_group_name        = azurerm_resource_group.rg.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.logs.id
  infrastructure_subnet_id   = azurerm_subnet.aca.id
  tags                       = local.tags
}

resource "azurerm_container_app_environment_storage" "shared" {
  name                         = "scriptgroup-files"
  container_app_environment_id = azurerm_container_app_environment.env.id
  account_name                 = azurerm_storage_account.files.name
  share_name                   = azurerm_storage_share.scriptgroup.name
  access_key                   = azurerm_storage_account.files.primary_access_key
  access_mode                  = "ReadWrite"
}

resource "azurerm_storage_account" "files" {
  name                     = "${var.project_name}${var.environment}files"
  resource_group_name      = azurerm_resource_group.rg.name
  location                 = azurerm_resource_group.rg.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  tags                     = local.tags
}

resource "azurerm_storage_share" "scriptgroup" {
  name               = "scriptgroup-files"
  storage_account_id = azurerm_storage_account.files.id
  quota              = 1
}
