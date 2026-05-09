variable "administrator_password" {
  description = "Supplied at deploy time by a DOrc sensitive property; never committed."
  type        = string
  sensitive   = true
}

module "sql" {
  source = "../.."

  resource_group_name    = "rg-data-dev"
  location               = "westeurope"
  server_name            = "sql-app-dev"
  database_name          = "appdb"
  administrator_password = var.administrator_password

  sku_name = "S0"

  tags = {
    environment = "dev"
    owner       = "platform"
  }
}
