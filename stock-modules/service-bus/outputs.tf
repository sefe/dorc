output "namespace_id" {
  description = "Resource ID of the Service Bus namespace."
  value       = azurerm_servicebus_namespace.this.id
}

output "namespace_name" {
  description = "Name of the Service Bus namespace."
  value       = azurerm_servicebus_namespace.this.name
}

output "namespace_endpoint" {
  description = "Endpoint URL of the Service Bus namespace. No SAS connection strings are output; consumers should use Entra ID auth or a data block."
  value       = azurerm_servicebus_namespace.this.endpoint
}

output "queue_id" {
  description = "Resource ID of the queue."
  value       = azurerm_servicebus_queue.this.id
}

output "queue_name" {
  description = "Name of the queue."
  value       = azurerm_servicebus_queue.this.name
}
