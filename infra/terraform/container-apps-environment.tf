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
  min_tls_version          = "TLS1_2"
  # The Container Apps environment mounts the file share with access-key
  # auth over the public endpoint, and Terraform creates the share via the
  # data plane; both fail when public network access is disabled (no private
  # endpoint exists in this demo).
  public_network_access_enabled   = true
  allow_nested_items_to_be_public = false
  tags                            = local.tags
}

resource "azurerm_storage_share" "scriptgroup" {
  name = "scriptgroup-files"
  # storage_account_name (not storage_account_id) — the azurerm ~> 3.x
  # provider pinned in providers.tf requires the name-based reference.
  storage_account_name = azurerm_storage_account.files.name
  quota                = 1
}
