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
}

resource "azurerm_container_app_job" "runner" {
  name                         = "caj-${local.resource_prefix}-runner"
  location                     = azurerm_resource_group.rg.location
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = azurerm_resource_group.rg.name
  replica_timeout_in_seconds   = 1800
  replica_retry_limit          = 0
  tags                         = local.tags

  manual_trigger_config {
    parallelism              = 1
    replica_completion_count = 1
  }

  template {
    container {
      name   = "dorc-runner"
      image  = "${azurerm_container_registry.acr.login_server}/dorc-runner:${var.runner_image_tag}"
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "DOTNET_RUNNING_IN_CONTAINER"
        value = "true"
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
}
