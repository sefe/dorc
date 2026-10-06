resource "azurerm_container_app" "api" {
  name                         = "ca-${local.resource_prefix}-api"
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = azurerm_resource_group.rg.name
  revision_mode                = "Single"
  tags                         = local.tags

  template {
    min_replicas = 0
    max_replicas = 2

    container {
      name   = "dorc-api"
      image  = "${azurerm_container_registry.acr.login_server}/dorc-api:${var.api_image_tag}"
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Azure"
      }
      env {
        name  = "AppSettings__OAuth2__Authority"
        value = "https://login.microsoftonline.com/${var.entra_tenant_id}/v2.0"
      }
      env {
        name  = "AppSettings__OAuth2__UiClientId"
        value = azuread_application.ui.client_id
      }
      env {
        name  = "AppSettings__OAuth2__UiRequestedScopes"
        value = "openid profile offline_access email api://dorc-api-${var.environment}/user_impersonation"
      }
      env {
        name  = "AppSettings__OAuth2__ApiResourceName"
        value = "api://dorc-api-${var.environment}"
      }
      env {
        name  = "AppSettings__OAuth2__ApiGlobalScope"
        value = "api://dorc-api-${var.environment}/.default"
      }
      env {
        name        = "ConnectionStrings__DOrcConnectionString"
        secret_name = "sql-connection-string"
      }
      env {
        name        = "Azure__SignalR__ConnectionString"
        secret_name = "signalr-connection-string"
      }
      env {
        name  = "Azure__SignalR__IsUseAzureSignalR"
        value = "true"
      }

      # Probes target the liveness endpoint, which performs no dependency
      # checks: a SQL-backed probe would keep the serverless database awake
      # and defeat its auto-pause. /healthz/ready carries the SQL check and
      # is used by deployment verification instead.
      liveness_probe {
        path             = "/healthz"
        port             = 8080
        transport        = "HTTP"
        initial_delay    = 10
        interval_seconds = 30
      }

      readiness_probe {
        path             = "/healthz"
        port             = 8080
        transport        = "HTTP"
        initial_delay    = 5
        interval_seconds = 10
      }
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  registry {
    server               = azurerm_container_registry.acr.login_server
    username             = azurerm_container_registry.acr.admin_username
    password_secret_name = "acr-password"
  }

  secret {
    name  = "acr-password"
    value = azurerm_container_registry.acr.admin_password
  }

  secret {
    name  = "sql-connection-string"
    value = "Server=tcp:${azurerm_mssql_server.sql.fully_qualified_domain_name},1433;Database=${azurerm_mssql_database.db.name};User Id=${var.sql_admin_login};Password=${var.sql_admin_password};Encrypt=True;TrustServerCertificate=False;"
  }

  secret {
    name  = "signalr-connection-string"
    value = azurerm_signalr_service.signalr.primary_connection_string
  }

  # CI deploys new image tags with `az containerapp update`; without this a
  # later `terraform apply` would roll the app back to the tag held in state.
  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}

resource "azurerm_container_app" "monitor" {
  name                         = "ca-${local.resource_prefix}-monitor"
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = azurerm_resource_group.rg.name
  revision_mode                = "Single"
  tags                         = local.tags

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "dorc-monitor"
      image  = "${azurerm_container_registry.acr.login_server}/dorc-monitor:${var.monitor_image_tag}"
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Azure"
      }
      env {
        name  = "DOTNET_RUNNING_IN_CONTAINER"
        value = "true"
      }
      env {
        name        = "ConnectionStrings__DOrcConnectionString"
        secret_name = "sql-connection-string"
      }
      env {
        name  = "DORC_SCRIPTGROUP_FILES_PATH"
        value = "/var/log/dorc/scriptgroup-files/"
      }

      volume_mounts {
        name = "scriptgroup-files"
        path = "/var/log/dorc/scriptgroup-files"
      }
    }

    volume {
      name         = "scriptgroup-files"
      storage_name = azurerm_container_app_environment_storage.shared.name
      storage_type = "AzureFile"
    }
  }

  registry {
    server               = azurerm_container_registry.acr.login_server
    username             = azurerm_container_registry.acr.admin_username
    password_secret_name = "acr-password"
  }

  secret {
    name  = "acr-password"
    value = azurerm_container_registry.acr.admin_password
  }

  secret {
    name  = "sql-connection-string"
    value = "Server=tcp:${azurerm_mssql_server.sql.fully_qualified_domain_name},1433;Database=${azurerm_mssql_database.db.name};User Id=${var.sql_admin_login};Password=${var.sql_admin_password};Encrypt=True;TrustServerCertificate=False;"
  }

  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}

# NOTE: no runner Container App Job is provisioned. Dispatching deployment
# Runners is not yet supported in container mode (the Monitor's dispatch path
# is Windows-specific); a job that nothing can trigger would only suggest
# otherwise. The dorc-runner image is still built so the groundwork remains.