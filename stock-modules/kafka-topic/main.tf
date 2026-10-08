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
}

resource "aiven_kafka_topic" "this" {
  project                = var.project
  service_name           = var.service_name
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
