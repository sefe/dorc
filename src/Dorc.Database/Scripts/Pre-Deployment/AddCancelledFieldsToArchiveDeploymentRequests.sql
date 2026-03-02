/*
Pre-Deployment Script: Add CancelledBy and CancelledTime fields to ArchiveDeploymentRequests table
This script adds the new cancellation tracking fields to match the DeploymentRequest entity model.
*/

IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[deploy].[ArchiveDeploymentRequests]') 
    AND name = 'CancelledBy'
)
BEGIN
    PRINT 'Adding CancelledBy column to [deploy].[ArchiveDeploymentRequests]'
    
    ALTER TABLE [deploy].[ArchiveDeploymentRequests]
    ADD [CancelledBy] NVARCHAR(128) NULL
    
    PRINT 'CancelledBy column added successfully'
END
ELSE
BEGIN
    PRINT 'CancelledBy column already exists in [deploy].[ArchiveDeploymentRequests]'
END
GO

IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[deploy].[ArchiveDeploymentRequests]') 
    AND name = 'CancelledTime'
)
BEGIN
    PRINT 'Adding CancelledTime column to [deploy].[ArchiveDeploymentRequests]'
    
    ALTER TABLE [deploy].[ArchiveDeploymentRequests]
    ADD [CancelledTime] DATETIMEOFFSET(7) NULL
    
    PRINT 'CancelledTime column added successfully'
END
ELSE
BEGIN
    PRINT 'CancelledTime column already exists in [deploy].[ArchiveDeploymentRequests]'
END
GO

-- Verify the columns were added
IF EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[deploy].[ArchiveDeploymentRequests]') 
    AND name IN ('CancelledBy', 'CancelledTime')
)
BEGIN
    PRINT 'Verification: Cancellation fields successfully added to [deploy].[ArchiveDeploymentRequests]'
    
    SELECT 
        c.name AS ColumnName,
        t.name AS DataType,
        c.max_length AS MaxLength,
        c.is_nullable AS IsNullable
    FROM sys.columns c
    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'[deploy].[ArchiveDeploymentRequests]')
    AND c.name IN ('CancelledBy', 'CancelledTime')
END
GO