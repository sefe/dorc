resource "azurerm_signalr_service" "signalr" {
  name                = "sigr-${local.resource_prefix}"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.tags

  sku {
    name     = "Free_F1"
    capacity = 1
  }

  connectivity_logs_enabled = true
  messaging_logs_enabled    = true

  cors {
    allowed_origins = ["*"]
  }

  upstream_endpoint {
    category_pattern = ["*"]
    event_pattern    = ["*"]
    hub_pattern      = ["*"]
    url_template     = "https://${azurerm_container_app.api.ingress[0].fqdn}/hubs/deployments"
  }
}
