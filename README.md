# DOrc (DevOps Deployment Orchestrator)

## Overview

DOrc is a comprehensive DevOps deployment orchestration platform designed to manage PowerShell-based deployments across multiple environments. It provides a centralized solution for managing deployment configurations, monitoring execution, and ensuring consistent deployment processes across development, testing, and production environments.

### Key Features

- **PowerShell Deployment Management**: Execute and orchestrate PowerShell scripts across environments
- **Environment Configuration**: Centralized management of environment-specific configurations and variables
- **Web-based Dashboard**: Modern web interface for deployment management and monitoring
- **API-first Architecture**: RESTful API for integration with CI/CD pipelines and external tools
- **Real-time Monitoring**: Track deployment progress and system health
- **Azure DevOps Integration**: Built-in integration with Azure DevOps services
- **Role-based Access Control**: Secure access management with OIDC authentication
- **Audit Trail**: Comprehensive logging and monitoring of all deployment activities

## Architecture

DOrc consists of several interconnected components:

### Backend Components (.NET 8 / .NET Framework 4.8)

- **Dorc.Api**: Main REST API service providing deployment orchestration endpoints
- **Dorc.Core**: Core business logic and domain models
- **Dorc.Monitor**: System monitoring and health check service
- **Dorc.Runner**: Deployment execution engine for PowerShell scripts
- **Dorc.Database**: SQL Server database schema and migrations
- **Dorc.PersistentData**: Data access layer and Entity Framework configurations
- **Dorc.PowerShell**: PowerShell integration and script execution
- **Dorc.OpenSearchData**: Integration with OpenSearch for logging and analytics
- **CLI Tools**: Various command-line utilities for system management

### Frontend (TypeScript/Lit)

- **dorc-web**: Modern web application built with Lit framework and Vaadin components
- **Real-time UI**: Responsive interface for deployment management
- **Authentication**: OIDC-based authentication system
- **API Integration**: Type-safe API client generated from OpenAPI specifications

## Prerequisites

### Development Environment

- **Node.js**: >= 14.0.0
- **npm**: >= 7.0.0
- **Visual Studio 2022** or **Visual Studio Code** with .NET extensions
- **.NET 8 SDK** and **.NET Framework 4.8 Developer Pack**
- **SQL Server** or **SQL Server Express** for database
- **WiX Toolset v5** (for installer projects)

### Optional Dependencies

- **Azure DevOps Account** (for CI/CD integration)
- **OpenSearch/Elasticsearch** (for advanced logging)
- **IIS** (for production deployment)

## Getting Started

### 1. Clone the Repository

```bash
git clone https://github.com/sefe/dorc.git
cd dorc
```

### 2. Database Setup

1. Create a SQL Server database named `DOrc`
2. Update connection strings in configuration files:
   - `src/Dorc.Api/appsettings.Development.json`
   - `src/Dorc.Api/appsettings.json`

### 3. Backend Setup

#### Build the .NET Solution

```bash
# Navigate to source directory
cd src

# Restore NuGet packages
dotnet restore Dorc.sln

# Build the solution
dotnet build Dorc.sln --configuration Release
```

#### Run Database Migrations

```bash
# From the src directory
dotnet ef database update --project Dorc.PersistentData --startup-project Dorc.Api
```

#### Start the API Service

```bash
cd Dorc.Api
dotnet run
```

The API will be available at `https://localhost:7071` or `http://localhost:5071`

### 4. Frontend Setup

```bash
# Navigate to web application directory
cd src/dorc-web

# Install dependencies
npm install

# Start development server
npm run dev
```

The web application will be available at `http://localhost:8888`

## Development Workflow

### Building the Project

#### Backend Build
```bash
# Full solution build
dotnet build src/Dorc.sln --configuration Release

# Build specific project
dotnet build src/Dorc.Api --configuration Release
```

#### Frontend Build
```bash
cd src/dorc-web
npm run build
```

### Running Tests

#### Backend Tests
```bash
# Run all tests
dotnet test src/Dorc.sln

# Run specific test project
dotnet test src/Dorc.Api.Tests
dotnet test src/Dorc.Core.Tests
```

#### Frontend Tests
```bash
cd src/dorc-web
npm test
```

### Code Quality

#### Linting and Formatting
```bash
cd src/dorc-web
npm run format     # Format code with Prettier
npm run type-checking  # TypeScript and Lit type checking
```

## API Client Generation

DOrc uses OpenAPI specifications to generate type-safe client libraries.

### Generate TypeScript Client for DOrc API

```bash
cd src/dorc-web
npm run dorc-api-gen
```

This generates TypeScript RxJS-based API client from the Swagger specification.

### Generate C# Client for Azure DevOps

```bash
# From the appropriate directory containing build.json
openapi-generator-cli generate -g csharp -i .\build.json --skip-validate-spec
```

Azure DevOps OpenAPI specifications: https://github.com/MicrosoftDocs/vsts-rest-api-specs

## Deployment

### Development Deployment

1. Start the backend API service
2. Start the web development server
3. Configure authentication settings
4. Set up database connections

### Production Deployment

1. **Build Release Artifacts**:
   ```bash
   dotnet publish src/Dorc.Api --configuration Release --output ./publish/api
   cd src/dorc-web && npm run build
   ```

2. **Deploy to IIS**: Use the WiX installer projects (`Setup.Dorc`) or manually deploy to IIS

3. **Configure Production Settings**:
   - Update `appsettings.json` with production database connections
   - Configure OIDC authentication providers
   - Set up SSL certificates

### Using the Installer

WiX-based installers are available in the `Setup.Dorc` and `Setup.Acceptance` projects:

```bash
# Build installer (requires WiX Toolset v5)
dotnet build src/Setup.Dorc --configuration Release
```

## Load Testing

DOrc includes K6-based performance tests for API and web interface testing.

### Install K6

```bash
# Windows
winget install k6 --source winget
```

### Run Tests

```bash
# Update test-config.json with appropriate base URL
# Update users.json with test user credentials

# Run basic test
k6 run k6-tests/monitor-request-page-test.js

# Run with web dashboard
k6 run --out web-dashboard ./k6-tests/load-test-many-users.js
```

Access the web dashboard at http://127.0.0.1:5665/

### Troubleshooting K6

If you encounter HTTP_1_1_REQUIRED errors, set the environment variable:
```bash
# Windows
SET GODEBUG=http2client=0
```

## Configuration

### Environment-Specific Configuration

Create environment-specific configuration files:
- `src/dorc-web/src/config.development.ts`
- `src/dorc-web/src/config.staging.ts`
- `src/dorc-web/src/config.production.ts`

### Authentication Configuration

Configure OIDC authentication in `appsettings.json`:
```json
{
  "Authentication": {
    "Authority": "your-identity-provider",
    "ClientId": "dorc-client",
    "Scope": "openid profile dorc-api"
  }
}
```

## Monitoring and Logging

DOrc includes comprehensive monitoring capabilities:

### Application Monitoring

- **Health Checks**: Built-in health check endpoints for API and database connectivity
- **Metrics**: Performance metrics collection and reporting
- **Log4net Integration**: Structured logging throughout the application

### OpenSearch Integration

Configure OpenSearch for advanced log analysis:
1. Set up OpenSearch cluster
2. Update configuration in `appsettings.json`
3. Deploy `Dorc.OpenSearchData` service

## CLI Tools

Several command-line tools are included for system administration:

- **Tools.RequestCLI**: Execute deployment requests programmatically
- **Tools.PropertyValueCreationCLI**: Manage configuration properties
- **Tools.DeployCopyEnvBuildCLI**: Environment and build management
- **Tools.PostRestoreEndurCLI**: Post-deployment validation tools
- **ManagePropertiesCmdlet**: PowerShell cmdlet for property management

## Troubleshooting

### Common Issues

1. **Database Connection Issues**:
   - Verify SQL Server is running
   - Check connection strings in appsettings files
   - Ensure database exists and migrations are applied

2. **Web Application Not Loading**:
   - Check API service is running
   - Verify CORS configuration
   - Check browser developer console for errors

3. **Authentication Failures**:
   - Verify OIDC provider configuration
   - Check client ID and secret
   - Ensure redirect URLs are correctly configured

4. **PowerShell Execution Issues**:
   - Verify PowerShell execution policy settings
   - Check script permissions and paths
   - Review PowerShell module dependencies

### Development Tips

1. **Hot Reload**: Use `npm run dev` for frontend development with hot reload
2. **API Debugging**: Use Visual Studio debugger for backend API debugging
3. **Database Changes**: Always create Entity Framework migrations for schema changes
4. **Client Generation**: Regenerate API clients after swagger.json changes

## Additional Resources

- **Internal Wiki**: https://wiki/display/gdq/DOrc+Help
- **Issue Tracking**: Use GitHub Issues for bug reports and feature requests
- **API Documentation**: Available at `/swagger` endpoint when API is running

## Contributions

SEFE welcomes contributions to this solution. Please refer to the [CONTRIBUTING.md](CONTRIBUTING.md) file for detailed contribution guidelines.

### Development Workflow

1. Fork the repository and create your branch from `main`
2. Ensure code follows existing patterns and conventions  
3. Add tests for new functionality
4. Update documentation as needed
5. Ensure all tests pass and code builds successfully
6. Submit a pull request with clear description of changes

## Authors

The solution is designed and built by SEFE Securing Energy for Europe GmbH.

**SEFE** - [Visit us online](https://www.sefe.eu/)

## License

This project is licensed under the [Apache 2.0](LICENSE.md) License - see the LICENSE.md file for details.
