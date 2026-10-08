module "service_bus" {
  source = "../.."

  resource_group_name = "rg-messaging-dev"
  location            = "westeurope"
  namespace_name      = "sb-app-dev"
  queue_name          = "orders"

  sku                = "Standard"
  max_delivery_count = 10

  tags = {
    environment = "dev"
    owner       = "platform"
  }
}
