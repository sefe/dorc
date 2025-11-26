-- Add Terraform source configuration columns if they don't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[deploy].[Component]') AND name = 'TerraformSourceType')
BEGIN
    ALTER TABLE [deploy].[Component] ADD [TerraformSourceType] INT NOT NULL DEFAULT 0;
    PRINT 'Added TerraformSourceType column to Component table';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[deploy].[Component]') AND name = 'TerraformGitRepoUrl')
BEGIN
    ALTER TABLE [deploy].[Component] ADD [TerraformGitRepoUrl] NVARCHAR(512) NULL;
    PRINT 'Added TerraformGitRepoUrl column to Component table';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[deploy].[Component]') AND name = 'TerraformGitBranch')
BEGIN
    ALTER TABLE [deploy].[Component] ADD [TerraformGitBranch] NVARCHAR(256) NULL;
    PRINT 'Added TerraformGitBranch column to Component table';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[deploy].[Component]') AND name = 'TerraformGitPath')
BEGIN
    ALTER TABLE [deploy].[Component] ADD [TerraformGitPath] NVARCHAR(512) NULL;
    PRINT 'Added TerraformGitPath column to Component table';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[deploy].[Component]') AND name = 'TerraformArtifactBuildId')
BEGIN
    ALTER TABLE [deploy].[Component] ADD [TerraformArtifactBuildId] INT NULL;
    PRINT 'Added TerraformArtifactBuildId column to Component table';
END
