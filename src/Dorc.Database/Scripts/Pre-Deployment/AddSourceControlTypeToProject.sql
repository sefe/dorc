/*
Pre-Deployment Script: Add SourceControlType column to Project table
This script adds the SourceControlType column to support GitHub Actions
alongside Azure DevOps as a build server source. Defaults to 0 (AzureDevOps).
*/

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[deploy].[Project]')
    AND name = 'SourceControlType'
)
BEGIN
    PRINT 'Adding SourceControlType column to [deploy].[Project]'

    ALTER TABLE [deploy].[Project]
    ADD [SourceControlType] INT NOT NULL DEFAULT 0

    PRINT 'SourceControlType column added successfully'
END
ELSE
BEGIN
    PRINT 'SourceControlType column already exists in [deploy].[Project]'
END
GO
