# Development environment configuration
resource_group_name = "rg-myapp-dev"
location           = "East US"
environment        = "dev"

# Enable SQL Database for dev
enable_sql_database = true
database_name      = "myapp-dev-db"
sql_server_name    = "myapp-dev-sql"

# Disable SQL MI for dev (cost optimization)
enable_sql_mi      = false

tags = {
  Environment = "dev"
  Project     = "MyApp"
  Owner       = "DevTeam"
}