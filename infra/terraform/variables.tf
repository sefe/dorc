variable "location" {
  description = "Azure region for all resources"
  type        = string
  default     = "westeurope"
}

variable "environment" {
  description = "Environment name (e.g. demo, staging)"
  type        = string
  default     = "demo"
}

variable "project_name" {
  description = "Project name used for resource naming"
  type        = string
  default     = "dorc"
}

variable "sql_admin_login" {
  description = "SQL Server admin login name"
  type        = string
  default     = "dorcadmin"
}

variable "sql_admin_password" {
  description = "SQL Server admin password"
  type        = string
  sensitive   = true
}

variable "custom_domain" {
  description = "Custom domain for the demo (e.g. dorc-demo.example.com)"
  type        = string
  default     = ""
}

variable "entra_tenant_id" {
  description = "Azure Entra ID tenant ID"
  type        = string
}

variable "api_image_tag" {
  description = "Docker image tag for dorc-api"
  type        = string
  default     = "latest"
}

variable "monitor_image_tag" {
  description = "Docker image tag for dorc-monitor"
  type        = string
  default     = "latest"
}

variable "runner_image_tag" {
  description = "Docker image tag for dorc-runner"
  type        = string
  default     = "latest"
}

locals {
  resource_prefix = "${var.project_name}-${var.environment}"
  tags = {
    Environment = var.environment
    Project     = var.project_name
    ManagedBy   = "terraform"
  }
}
