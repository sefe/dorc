# Terraform Source Configuration Implementation

## Overview
This implementation adds support for retrieving Terraform code from Git repositories and Azure DevOps build artifacts, in addition to the existing shared folder approach.

## Configuration

### Required DOrc Configuration Values

Add the following configuration values to DOrc:

1. **For Git Source Type:**
   - `TerraformGitUsername`: Username or Personal Access Token (PAT) for Git authentication
   - `TerraformGitPassword`: Password or Personal Access Token (PAT) for Git authentication

2. **For Azure Artifact Source Type:**
   - `TerraformAzureDevOpsCollection`: Azure DevOps collection/organization name
   - `TerraformAzureDevOpsProject`: Azure DevOps project name

### Component Configuration (via JSON Editor)

When editing a Terraform component via the RefData JSON editor, use the following properties:

```json
{
  "ComponentType": 1,  // 1 = Terraform
  "TerraformSourceType": 1,  // 0 = SharedFolder, 1 = Git, 2 = AzureArtifact
  
  // For Git source:
  "TerraformGitRepoUrl": "https://github.com/your-org/terraform-repo.git",
  "TerraformGitBranch": "main",
  "TerraformGitPath": "path/to/terraform/files",  // Optional, defaults to repository root
  
  // For Azure Artifact source:
  "TerraformArtifactBuildId": 12345
}
```

## Usage Examples

### Example 1: Git Repository (GitHub)
```json
{
  "ComponentId": 100,
  "ComponentName": "Infrastructure Setup",
  "ComponentType": 1,
  "TerraformSourceType": 1,
  "TerraformGitRepoUrl": "https://github.com/myorg/infrastructure.git",
  "TerraformGitBranch": "production",
  "TerraformGitPath": "terraform/azure"
}
```

### Example 2: Git Repository (Azure DevOps)
```json
{
  "ComponentId": 101,
  "ComponentName": "Network Configuration",
  "ComponentType": 1,
  "TerraformSourceType": 1,
  "TerraformGitRepoUrl": "https://dev.azure.com/myorg/myproject/_git/terraform-configs",
  "TerraformGitBranch": "main",
  "TerraformGitPath": null
}
```

### Example 3: Azure DevOps Build Artifact
```json
{
  "ComponentId": 102,
  "ComponentName": "App Infrastructure",
  "ComponentType": 1,
  "TerraformSourceType": 2,
  "TerraformArtifactBuildId": 54321
}
```

### Example 4: Shared Folder (Legacy)
```json
{
  "ComponentId": 103,
  "ComponentName": "Legacy Setup",
  "ComponentType": 1,
  "TerraformSourceType": 0,
  "ScriptPath": "terraform\\legacy-setup"
}
```

## Architecture

### Source Provider Pattern
The implementation uses a provider pattern with the following components:

- **ITerraformSourceProvider**: Interface for all source providers
- **SharedFolderSourceProvider**: Retrieves from shared folder (legacy behavior)
- **GitSourceProvider**: Clones from Git repositories (GitHub, Azure DevOps Git)
- **AzureArtifactSourceProvider**: Downloads from Azure DevOps build artifacts
- **TerraformSourceProviderFactory**: Factory for creating appropriate providers

### Flow
1. TerraformDispatcher reads component configuration
2. TerraformSourceProviderFactory creates appropriate provider based on TerraformSourceType
3. Provider retrieves source to a temporary directory
4. TerraformRunner processes the Terraform code
5. Temporary directory is cleaned up after deployment

## Security Considerations

1. **Credential Handling**: 
   - Git credentials are stored in DOrc config, not in component definitions
   - Credentials are URL-escaped when passed to git
   - All logging sanitizes URLs and errors to prevent credential leakage

2. **Temporary Files**:
   - Source code is retrieved to temporary directories
   - Directories are automatically cleaned up after deployment
   - Each deployment uses a unique temporary directory

3. **No Caching**:
   - Source code is re-retrieved on every deployment
   - This ensures latest code is always used
   - No stale cached content

## Database Schema Changes

New columns added to `deploy.Component` table:
- `TerraformSourceType` (INT, NOT NULL, DEFAULT 0)
- `TerraformGitRepoUrl` (NVARCHAR(512), NULL)
- `TerraformGitBranch` (NVARCHAR(256), NULL)
- `TerraformGitPath` (NVARCHAR(512), NULL)
- `TerraformArtifactBuildId` (INT, NULL)

Migration script automatically adds columns if they don't exist.

## Troubleshooting

### Git Clone Failures
- Verify Git credentials are correct in DOrc config
- Check that the repository URL is accessible
- Ensure the branch name is correct
- Check logs for sanitized error messages

### Azure Artifact Download Failures
- Verify TerraformAzureDevOpsCollection and TerraformAzureDevOpsProject are configured
- Check that the build ID exists and has artifacts
- Ensure Azure DevOps authentication is working
- Check DOrc Monitor logs for details

### General Issues
- Check DOrc Monitor logs for detailed error messages
- Verify component configuration JSON is valid
- Ensure temporary directory has sufficient permissions
- Check available disk space for source downloads

## Future Enhancements

Potential improvements for future versions:
- Support for SSH keys instead of PAT for Git
- Caching with version detection
- Support for Git tags in addition to branches
- Multiple artifact sources per component
- Terraform module library support
