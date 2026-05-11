# Custom domain configuration (optional)
# Only created if custom_domain is set

resource "azurerm_container_app_custom_domain" "api" {
  count                                      = var.custom_domain != "" ? 1 : 0
  name                                       = var.custom_domain
  container_app_id                           = azurerm_container_app.api.id
  container_app_environment_certificate_id   = null # Uses managed certificate
  certificate_binding_type                   = "SniEnabled"
}
