terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aiven = {
      source  = "aiven/aiven"
      version = ">= 4.42.0, < 5.0.0"
    }
  }
}
