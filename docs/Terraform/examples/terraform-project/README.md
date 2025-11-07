# Example Terraform Project for DOrc

This directory contains a complete example of a Terraform project configured to work with DOrc's Terraform Runner.

## Project Structure

```
terraform-project/
├── main.tf                    # Main Terraform configuration
├── variables.tf               # Variable definitions
├── environments/              # Environment-specific configurations
│   ├── dev/
│   │   └── terraform.tfvars
│   ├── staging/
│   │   └── terraform.tfvars
│   └── prod/
│       └── terraform.tfvars
└── modules/                   # Reusable Terraform modules
    ├── sql-database/
    │   ├── main.tf
    │   ├── variables.tf
    │   └── outputs.tf
    └── sql-managed-instance/
        ├── main.tf
        ├── variables.tf
        └── outputs.tf
```

## How to Use with DOrc

1. **Upload Files**: Place this project structure in your DOrc script repository
2. **Create Components**: 
   - Component Name: "SQL Database Infrastructure"
   - Component Type: "Terraform"
   - Script Path: Path to this directory
3. **Configure Properties**: Set up environment-specific properties in DOrc that match the variables in `variables.tf`
4. **Deploy**: Create deployment requests through DOrc UI

## Key Features

- **Environment Separation**: Different configurations for dev/staging/prod
- **Modular Design**: Reusable modules for SQL Database and Managed Instance
- **Variable-Driven**: All environment-specific values are parameterized
- **DOrc Integration**: Designed to work seamlessly with DOrc's property system

See the parent documentation for detailed setup instructions.