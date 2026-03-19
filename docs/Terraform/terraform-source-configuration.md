# Terraform Sources and Deployment Flow in DOrc

This document explains how DOrc fetches and runs Terraform code, and how to configure different Terraform source types (SharedFolder, Git, AzureArtifact) on projects and components.

## Overview

DOrc runs Terraform deployments in two phases:

1. **Plan phase**: DOrc prepares a working directory, fetches Terraform code from the configured source, runs `terraform init` and `terraform plan`, then stores the binary plan and a human-readable plan in storage.
2. **Apply phase**: After you review and confirm the plan, DOrc replays the stored plan with `terraform apply`.

Terraform code can come from one of three source types per component:

- **SharedFolder**: Code lives on a file share or local folder, referenced via ScriptPath.
- **Git**: Code is cloned from a Git repository (GitHub or Azure DevOps Git) configured on the project.
- **AzureArtifact**: Code is downloaded from Azure DevOps build artifacts produced by your CI pipeline.

The source type is configured on the component; project-level settings provide shared configuration such as the Git repository URL.

## Data Model: Projects and Components

### Project-level fields

Projects hold configuration that is shared by multiple components:

- `ArtefactsUrl`: Base URL of Azure DevOps organization (used for AzureArtifact and sometimes SharedFolder scenarios).
- `ArtefactsSubPaths`: Semicolon-separated list of Azure DevOps project names used when fetching build artifacts.
- `ArtefactsBuildRegex`: Regex used by DOrc when selecting builds.
- `TerraformGitRepoUrl`: Git repository URL for Terraform code (for Git source type).

Example:

```json
"Project": {
  "ProjectId": 100,
  "ProjectName": "Terraform infrastructure",
  "ProjectDescription": "",
  "ArtefactsUrl": "https://dev.azure.com/example-org/",
  "ArtefactsSubPaths": "Infra Project",
  "ArtefactsBuildRegex": "",
  "TerraformGitRepoUrl": "https://example-org@dev.azure.com/example-org/Infra%20Project/_git/infra-terraform",
  "SourceDatabase": null
}
```

### Component-level fields

Components represent individual deployment units. For Terraform components you typically configure:

- `ComponentType`: must be `"Terraform"` for Terraform components.
- `ScriptPath`: a path to Terraform code for SharedFolder; may be left empty for Git/AzureArtifact.
- `TerraformSourceType`: source type for the code: `SharedFolder`, `Git`, or `AzureArtifact`.
- `TerraformGitBranch`: branch name for Git source (optional; defaults to `main` when empty).
- `TerraformSubPath`: optional relative path within the repo, artifact, or shared folder to use as Terraform root.

A single project can mix components with different source types. For example:

```json
"Components": [
  {
    "ComponentId": 1,
    "ComponentName": "TF AzureArtifact example",
    "ScriptPath": "",
    "NonProdOnly": true,
    "StopOnFailure": false,
    "ParentId": 0,
    "IsEnabled": true,
    "PSVersion": null,
    "ComponentType": "Terraform",
    "TerraformSourceType": "AzureArtifact",
    "TerraformGitBranch": null,
    "TerraformSubPath": "",
    "Children": []
  },
  {
    "ComponentId": 2,
    "ComponentName": "TF Git example",
    "ScriptPath": "",
    "NonProdOnly": true,
    "StopOnFailure": false,
    "ParentId": 0,
    "IsEnabled": true,
    "PSVersion": null,
    "ComponentType": "Terraform",
    "TerraformSourceType": "Git",
    "TerraformGitBranch": null,
    "TerraformSubPath": "terraform-project",
    "Children": []
  },
  {
    "ComponentId": 3,
    "ComponentName": "TF SharedFolder example",
    "ScriptPath": "\\\\share\\Terraform\\0001-Terraform",
    "NonProdOnly": true,
    "StopOnFailure": false,
    "ParentId": 0,
    "IsEnabled": true,
    "PSVersion": "v7",
    "ComponentType": "Terraform",
    "TerraformSourceType": "SharedFolder",
    "TerraformGitBranch": null,
    "TerraformSubPath": "terraform-project",
    "Children": []
  }
]
```

## Source Type: SharedFolder

### When to use

Use **SharedFolder** when your Terraform code is stored on a file share or local directory that is directly accessible to the DOrc Runner host.

### Configuration

- Project:
  - `ArtefactsUrl`, `ArtefactsSubPaths`, `ArtefactsBuildRegex` are optional for SharedFolder.
  - `TerraformGitRepoUrl` is not required.
- Component:
  - `ComponentType` = `Terraform`.
  - `TerraformSourceType` = `SharedFolder` (default value for new Terraform components).
  - `ScriptPath` = path to Terraform files (for example `C:\\\\0001-Terraform\\terraform-project`).
  - `TerraformSubPath` (optional) = relative subdirectory inside `ScriptPath` to use as Terraform root.

If `TerraformSubPath` is set, DOrc trims the working directory to just that subfolder before running `terraform init/plan/apply`.

### Execution behavior

1. Monitor resolves the full script path using ScriptPath.
2. The SharedFolder provider copies the directory tree into a temporary working directory.
3. If `TerraformSubPath` is set, only that subfolder is used as the Terraform root.
4. Terraform is executed in that working directory.

## Source Type: Git

### When to use

Use **Git** when you want Terraform code to be cloned from a Git repository (for example Azure DevOps Git or GitHub). This keeps code versioned in Git and avoids manually copying files to a share.

### Configuration

**Project-level (Git settings)**

- `TerraformGitRepoUrl` must be set to your Git repository URL, for example:

  - GitHub: `https://github.com/example-org/infra-terraform.git`
  - Azure DevOps Git: `https://example-org@dev.azure.com/example-org/Infra%20Project/_git/infra-terraform`

**Component-level (Git usage)**

For each Terraform component that uses Git:

- `ComponentType` = `Terraform`.
- `TerraformSourceType` = `Git`.
- `TerraformGitBranch` (optional) = branch name. If empty, DOrc defaults to `main`.
- `TerraformSubPath` (optional) = subdirectory inside the repo that contains the Terraform project (for example `terraform-project` or `environments/dev`).
- `ScriptPath` can usually be left empty for Git source. The Git provider uses the repository and branch; `TerraformSubPath` narrows the Terraform root.

### Authentication

Git access can be configured in two ways:

- **Personal Access Token (PAT)**:
  - Create a DOrc property named `Terraform_Git_PAT` (for example, on the environment or project).
  - The Monitor service picks up this property and passes it to the Git source provider.
  - The provider uses the PAT as credentials for cloning.

- **Azure DevOps bearer token** (for Azure DevOps Git URLs):
  - If the repository URL is an Azure DevOps URL, DOrc can obtain a bearer token using Entra ID (tenant, client ID, client secret) configured in DOrc settings.
  - The Git provider uses this token when a PAT is not supplied.

### Execution behavior

1. Monitor builds a ScriptGroup and includes:
   - `TerraformGitRepoUrl` from the project.
   - `TerraformGitBranch` and `TerraformSubPath` from the component.
   - `Terraform_Git_PAT` or Azure bearer token if available.
2. The Git provider clones the repository into the working directory using the specified branch.
3. If `TerraformSubPath` is set, DOrc narrows the working directory to that subfolder.
4. Terraform commands run in that directory.

## Source Type: AzureArtifact

### When to use

Use **AzureArtifact** when Terraform code is produced and packaged by an Azure DevOps pipeline as build artifacts. This is useful when you already have CI pipelines that prepare Terraform code bundles.

### Configuration

**Project-level (Azure DevOps settings)**

- `ArtefactsUrl`: Azure DevOps organization URL, for example:
  - `https://dev.azure.com/example-org/`
- `ArtefactsSubPaths`: semicolon-separated list of Azure DevOps project names that may hold artifacts, for example:
  - `"Infra Project"`
- `ArtefactsBuildRegex`: optional regex for build selection.
- `TerraformGitRepoUrl`: optional; not required for pure artifact-based deployments, but can be set if you also use Git source type.

**Component-level (AzureArtifact usage)**

For each Terraform component that uses AzureArtifact:

- `ComponentType` = `Terraform`.
- `TerraformSourceType` = `AzureArtifact`.
- `TerraformSubPath` (optional) = subdirectory inside the artifact where Terraform root lives.
- `ScriptPath` can be empty when code comes solely from artifacts.

**Request-level (build context)**

If the source type is `AzureArtifact`, DOrc will take the artifact from the selected by user build, from  `DropLocation` if it is a folder path (`file://` URI).
If there are several artifacts available and no `DropLocation` was set, then the priority-based selection mechanism is in place which defines the artifact type will be choosen to download artifact from.
The default artifact type priority is: **`filepath,Container,PipelineArtifact`**

This means:
1. First, try to download artifacts of type `filepath`
2. If not found, try `Container` type
3. If still not found, try `PipelineArtifact` type
4. If still not found, use first artifact with DownloadURL available

DOrc also obtains an Azure DevOps bearer token from Entra configuration to authorize artifact downloads.

### Execution behavior

1. Monitor inspects the deployment request, project, and component:
   - Extracts Azure organization and project from `ArtefactsUrl` and `ArtefactsSubPaths`.
   - Parses `BuildUri` to obtain the build ID.
   - Sets `ScriptsLocation` and Azure-specific fields on the ScriptGroup.
2. The AzureArtifact provider does one of:
   - If `ScriptsLocation` is a `file://` path:
     - Treats it as a directory or zip file on disk and copies/extracts it into the working directory.
   - Otherwise:
     - Uses the Azure DevOps REST API to locate artifacts for the build.
     - Selects an artifact (by priority: `filepath,Container,PipelineArtifact`).
     - Downloads and extracts the artifact archive into the working directory.
3. If `TerraformSubPath` is set, DOrc narrows the working directory to that subfolder.
4. Terraform commands run in that directory.

## TerraformSubPath Semantics

`TerraformSubPath` is always interpreted as a relative path inside the fetched source:

- For **SharedFolder**: relative to the folder resolved from  ScriptPath.
- For **Git**: relative to the root of the cloned repository.
- For **AzureArtifact**: relative to the root of the extracted build artifact.

Validation rules (enforced by DOrc):

- Must not be an absolute path.
- Must not contain `..` path segments.
- Must not contain invalid path characters.

Examples:

- `terraform-project`
- `infra/sql-database`
- `environments/dev`

Use `TerraformSubPath` when you want one repository or artifact to contain multiple Terraform components in different subfolders.

## End-to-End Examples

### Example 1: Git-based Terraform component

1. **Project**
   - Set `TerraformGitRepoUrl` to a Git repository that contains your Terraform code.
2. **Component**
   - `ComponentType` = `Terraform`.
   - `TerraformSourceType` = `Git`.
   - `TerraformGitBranch` = `main`.
   - `TerraformSubPath` = `terraform-project`.
3. **Properties**
   - Configure `Terraform_Git_PAT` as a secure property if the repository requires a PAT.
4. **Deployment**
   - Create a deployment request for this component and target environment.
   - DOrc clones the repo, narrows to `terraform-project`, generates a plan, and waits for your confirmation.

### Example 2: AzureArtifact-based Terraform component

1. **Project**
   - `ArtefactsUrl` = `https://dev.azure.com/example-org/`.
   - `ArtefactsSubPaths` = `"Infra Project"`.
2. **CI Pipeline**
   - Azure DevOps pipeline builds and publishes an artifact containing your Terraform code.
3. **Component**
   - `ComponentType` = `Terraform`.
   - `TerraformSourceType` = `AzureArtifact`.
   - `TerraformSubPath` = `terraform-project` (if the Terraform root is inside that subfolder).
4. **Deployment Request**
   - Includes `BuildUri` pointing to the Azure DevOps build.
5. **Execution**
   - DOrc downloads the artifact for the given build, extracts it, narrows to `terraform-project`, and runs Terraform from there.

### Example 3: SharedFolder-based Terraform component

1. **Component**
   - `ComponentType` = `Terraform`.
   - `TerraformSourceType` = `SharedFolder`.
   - `ScriptPath` = `\\\\share\\Terraform\\0001-Terraform`.
   - `TerraformSubPath` = `terraform-project`.
2. **Execution**
   - DOrc uses ScriptPath, copies files from `\\\\share\\Terraform\\0001-Terraform`, then narrows to `terraform-project`.

## Other Notes

For a step-by-step example using a simple Terraform project structure, see `terraform-setup-example.md`. For a concrete sample project layout, see `examples/terraform-project/README.md`.
