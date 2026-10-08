module "clickhouse_database" {
  source = "../.."

  project       = "trading-analytics"
  service_name  = "clickhouse-analytics-dev"
  database_name = "appdb"

  # Throwaway example environment; keep protection on in production.
  termination_protection = false
}
