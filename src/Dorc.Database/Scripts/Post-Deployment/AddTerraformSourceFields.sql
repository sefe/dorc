-- Add TerraformGitRepoUrl and TerraformSubPath to Project table if they don't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[deploy].[Project]') AND name = 'TerraformGitRepoUrl')
BEGIN
    ALTER TABLE [deploy].[Project]
    ADD [TerraformGitRepoUrl] NVARCHAR(512) NULL;
    PRINT 'Added TerraformGitRepoUrl column to Project table';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[deploy].[Project]') AND name = 'TerraformSubPath')
BEGIN
    ALTER TABLE [deploy].[Project]
    ADD [TerraformSubPath] NVARCHAR(512) NULL;
    PRINT 'Added TerraformSubPath column to Project table';
END
GO

-- Add TerraformSourceType and TerraformGitBranch to Component table if they don't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[deploy].[Component]') AND name = 'TerraformSourceType')
BEGIN
    ALTER TABLE [deploy].[Component]
    ADD [TerraformSourceType] INT NOT NULL DEFAULT 0;
    PRINT 'Added TerraformSourceType column to Component table';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[deploy].[Component]') AND name = 'TerraformGitBranch')
BEGIN
    ALTER TABLE [deploy].[Component]
    ADD [TerraformGitBranch] NVARCHAR(256) NULL;
    PRINT 'Added TerraformGitBranch column to Component table';
END
GO
