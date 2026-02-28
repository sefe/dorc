resource "azurerm_resource_group" "rg" {
  name     = "rg-${local.resource_prefix}"
  location = var.location
  tags     = local.tags
}
