output "topic_id" {
  description = "ID of the Kafka topic (project/service_name/topic_name)."
  value       = aiven_kafka_topic.this.id
}

output "topic_name" {
  description = "Full topic name composed per the SEFE naming standard."
  value       = aiven_kafka_topic.this.topic_name
}

output "project" {
  description = "Aiven project the topic belongs to."
  value       = aiven_kafka_topic.this.project
}

output "service_name" {
  description = "Kafka service the topic belongs to."
  value       = aiven_kafka_topic.this.service_name
}

output "partitions" {
  description = "Number of partitions on the topic."
  value       = aiven_kafka_topic.this.partitions
}
