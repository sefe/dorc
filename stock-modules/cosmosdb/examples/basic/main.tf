module "cosmosdb" {
  source = "../.."

  resource_group_name = "rg-data-dev"
  location            = "westeurope"
  account_name        = "cosmos-app-dev"
  database_name       = "appdb"

  consistency_level = "Session"
  throughput        = 400

  tags = {
    environment = "dev"
    owner       = "platform"
  }
}
