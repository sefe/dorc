output "database_id" {
  description = "ID of the ClickHouse database (project/service_name/name)."
  value       = aiven_clickhouse_database.this.id
}

output "database_name" {
  description = "Name of the created ClickHouse database."
  value       = aiven_clickhouse_database.this.name
}

output "project" {
  description = "Aiven project the database belongs to."
  value       = aiven_clickhouse_database.this.project
}

output "service_name" {
  description = "ClickHouse service the database belongs to."
  value       = aiven_clickhouse_database.this.service_name
}
