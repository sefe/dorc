module "storage" {
  source = "../.."

  resource_group_name = "rg-storage-dev"
  location            = "westeurope"
  account_name        = "stappdev0001"

  replication_type = "ZRS"

  tags = {
    environment = "dev"
    owner       = "platform"
  }
}
