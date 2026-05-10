output "vnet_id" {
  description = "Resource ID of the virtual network."
  value       = azurerm_virtual_network.this.id
}

output "vnet_name" {
  description = "Name of the virtual network."
  value       = azurerm_virtual_network.this.name
}

output "subnet_ids" {
  description = "Map of subnet name to subnet resource ID."
  value       = { for k, s in azurerm_subnet.this : k => s.id }
}

output "address_space" {
  description = "CIDR address space(s) of the virtual network (echoed for downstream consumers)."
  value       = azurerm_virtual_network.this.address_space
}
