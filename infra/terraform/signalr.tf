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

  # Demo tradeoff: the API serves the SPA from the same origin, so CORS is
  # left open rather than pinned to a FQDN that is not known until the
  # Container App exists.
  cors {
    allowed_origins = ["*"]
  }

  # No upstream_endpoint: the API uses the Azure SignalR SDK in Default mode,
  # where upstreams do not apply (they are a Serverless-mode concept). The
  # original upstream also referenced the API Container App's FQDN while the
  # app referenced this service's connection string — a dependency cycle.
}
