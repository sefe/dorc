module "vnet" {
  source = "../.."

  resource_group_name = "rg-network-dev"
  location            = "westeurope"
  vnet_name           = "vnet-app-dev"
  address_space       = ["10.20.0.0/16"]

  subnets = [
    { name = "snet-app", address_prefix = "10.20.1.0/24" },
    { name = "snet-db", address_prefix = "10.20.2.0/24" },
  ]

  tags = {
    environment = "dev"
    owner       = "platform"
  }
}
