resource "azurerm_servicebus_namespace" "this" {
  name                = var.namespace_name
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = var.sku
  minimum_tls_version = "1.2"

  public_network_access_enabled = var.public_network_access_enabled
  local_auth_enabled            = var.local_auth_enabled

  tags = var.tags
}

resource "azurerm_servicebus_queue" "this" {
  name         = var.queue_name
  namespace_id = azurerm_servicebus_namespace.this.id

  max_delivery_count  = var.max_delivery_count
  enable_partitioning = var.enable_partitioning
}
