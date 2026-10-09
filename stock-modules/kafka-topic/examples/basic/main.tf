module "kafka_topic" {
  source = "../.."

  # project defaults to trading-traveler; service_name is derived from
  # environment_tier (dv -> traveler-unstable-dev).

  # Composes topic name: tr.dv.gbl.traveler.trade-events.il2
  business_vertical = "tr"
  environment_tier  = "dv"
  scope             = "gbl"
  data_grouping     = "traveler"
  data_description  = "trade-events"
  integrity_level   = "il2"

  partitions          = 3
  replication         = 2
  cleanup_policy      = "delete"
  min_insync_replicas = 2
  retention_ms        = 86400000 # 1 day
  retention_bytes     = -1

  # Throwaway example environment; keep protection on in production.
  termination_protection = false
}
