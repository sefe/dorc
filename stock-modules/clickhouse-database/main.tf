resource "aiven_clickhouse_database" "this" {
  project                = var.project
  service_name           = var.service_name
  name                   = var.database_name
  termination_protection = var.termination_protection
}
