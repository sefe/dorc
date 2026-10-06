data "azuread_client_config" "current" {}

# API App Registration
resource "azuread_application" "api" {
  display_name = "dorc-api-${var.environment}"
  owners       = [data.azuread_client_config.current.object_id]

  identifier_uris = ["api://dorc-api-${var.environment}"]

  api {
    oauth2_permission_scope {
      admin_consent_description  = "Manage DOrc API"
      admin_consent_display_name = "Manage DOrc"
      enabled                    = true
      id                         = "00000000-0000-0000-0000-000000000001"
      type                       = "Admin"
      # Scope values must be simple names; the full URI form
      # (api://.../<value>) is composed by clients at request time, and
      # /.default is a request-time construct, not a scope value.
      value = "user_impersonation"
    }
  }

  web {
    implicit_grant {
      access_token_issuance_enabled = false
      id_token_issuance_enabled     = false
    }
  }

  required_resource_access {
    resource_app_id = "00000003-0000-0000-c000-000000000000" # Microsoft Graph

    resource_access {
      id   = "e1fe6dd8-ba31-4d61-89e7-88639da4683d" # User.Read
      type = "Scope"
    }
  }
}

resource "azuread_service_principal" "api" {
  client_id = azuread_application.api.client_id
  owners    = [data.azuread_client_config.current.object_id]
}

# Kept out of the application resource for the same cycle-avoidance reason as
# the SPA redirect URIs below.
resource "azuread_application_redirect_uris" "api_web" {
  application_id = azuread_application.api.id
  type           = "Web"

  redirect_uris = ["https://${azurerm_container_app.api.ingress[0].fqdn}/swagger/oauth2-redirect.html"]
}

resource "azuread_application_password" "api" {
  application_id = azuread_application.api.id
  display_name   = "terraform-managed"
  # Relative expiry (1 year from creation) instead of a fixed calendar date
  # that silently passes.
  end_date_relative = "8760h"
}

# SPA (UI) App Registration
#
# The SPA redirect URIs (which include the Container App's FQDN) live in a
# standalone azuread_application_redirect_uris resource rather than inline:
# the API Container App's environment references this application's client ID,
# so an inline FQDN reference here would create a dependency cycle.
resource "azuread_application" "ui" {
  display_name = "dorc-ui-${var.environment}"
  owners       = [data.azuread_client_config.current.object_id]

  required_resource_access {
    resource_app_id = azuread_application.api.client_id

    resource_access {
      id   = "00000000-0000-0000-0000-000000000001"
      type = "Scope"
    }
  }

  required_resource_access {
    resource_app_id = "00000003-0000-0000-c000-000000000000" # Microsoft Graph

    resource_access {
      id   = "e1fe6dd8-ba31-4d61-89e7-88639da4683d" # User.Read
      type = "Scope"
    }
    resource_access {
      id   = "37f7f235-527c-4136-accd-4a02d197296e" # openid
      type = "Scope"
    }
    resource_access {
      id   = "14dad69e-099b-42c9-810b-d002981feec1" # profile
      type = "Scope"
    }
    resource_access {
      id   = "64a6cdd6-aab1-4aaf-94b8-3cc8405e90d0" # email
      type = "Scope"
    }
  }
}

resource "azuread_application_redirect_uris" "ui_spa" {
  application_id = azuread_application.ui.id
  type           = "SPA"

  redirect_uris = [
    "https://${azurerm_container_app.api.ingress[0].fqdn}/signin-callback.html",
    "https://${azurerm_container_app.api.ingress[0].fqdn}/signout-callback.html",
    "http://localhost:8888/signin-callback.html",
  ]
}

# Monitor Service Principal (for API access)
resource "azuread_application" "monitor" {
  display_name = "dorc-monitor-${var.environment}"
  owners       = [data.azuread_client_config.current.object_id]

  required_resource_access {
    resource_app_id = azuread_application.api.client_id

    resource_access {
      id   = "00000000-0000-0000-0000-000000000001"
      type = "Scope"
    }
  }
}

resource "azuread_service_principal" "monitor" {
  client_id = azuread_application.monitor.client_id
  owners    = [data.azuread_client_config.current.object_id]
}

resource "azuread_application_password" "monitor" {
  application_id    = azuread_application.monitor.id
  display_name      = "terraform-managed"
  end_date_relative = "8760h"
}
