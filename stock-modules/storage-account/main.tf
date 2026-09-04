resource "azurerm_storage_account" "this" {
  name                = var.account_name
  resource_group_name = var.resource_group_name
  location            = var.location

  account_tier             = var.account_tier
  account_replication_type = var.replication_type
  min_tls_version          = var.min_tls_version

  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = true
  public_network_access_enabled   = var.public_network_access_enabled

  tags = var.tags
}
