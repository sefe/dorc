# Topic name per the SEFE Kafka messaging standard; optional trailing
# segments are dropped when empty.
locals {
  topic_name = join(".", compact([
    var.business_vertical,
    var.environment_tier,
    var.scope,
    var.data_grouping,
    var.data_description,
    var.integrity_level,
    var.origin,
    var.publisher_identifier,
  ]))

  # SEFE traveler Kafka instances (Aiven BYOC on Azure uksouth). When no
  # instance is named explicitly, the environment tier picks it.
  service_by_tier = {
    dv = "traveler-unstable-dev"
    ut = "traveler-non-prod"
    qa = "traveler-non-prod"
    pp = "traveler-non-prod"
    pr = "traveler-production"
  }
  service_name = var.service_name != "" ? var.service_name : local.service_by_tier[var.environment_tier]
}

resource "aiven_kafka_topic" "this" {
  project                = var.project
  service_name           = local.service_name
  topic_name             = local.topic_name
  partitions             = var.partitions
  replication            = var.replication
  termination_protection = var.termination_protection

  config {
    cleanup_policy      = var.cleanup_policy
    min_insync_replicas = var.min_insync_replicas
    retention_bytes     = var.retention_bytes
    retention_ms        = var.retention_ms
  }
}
