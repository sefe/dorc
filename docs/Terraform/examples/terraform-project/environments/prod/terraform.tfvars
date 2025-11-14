# Production environment configuration
resource_group_name = "rg-myapp-prod"
location           = "East US"
environment        = "prod"

# Enable both SQL Database and Managed Instance for prod
enable_sql_database = true
database_name      = "myapp-prod-db"
sql_server_name    = "myapp-prod-sql"

enable_sql_mi      = true
sql_mi_name       = "myapp-prod-mi"

tags = {
  Environment = "prod"
  Project     = "MyApp"
  Owner       = "ProdTeam"
  Criticality = "High"
}