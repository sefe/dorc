/*
Pre-Deployment Script: Add CancelledBy and CancelledTime fields to ArchiveDeploymentRequests table
This script adds the new cancellation tracking fields to match the DeploymentRequest entity model.
*/

IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[archive].[DeploymentRequest]') 
    AND name = 'CancelledBy'
)
BEGIN
    PRINT 'Adding CancelledBy column to [archive].[DeploymentRequest]'
    
    ALTER TABLE [archive].[DeploymentRequest]
    ADD [CancelledBy] NVARCHAR(128) NULL
    
    PRINT 'CancelledBy column added successfully'
END
ELSE
BEGIN
    PRINT 'CancelledBy column already exists in [archive].[DeploymentRequest]'
END
GO

IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[archive].[DeploymentRequest]') 
    AND name = 'CancelledTime'
)
BEGIN
    PRINT 'Adding CancelledTime column to [archive].[DeploymentRequest]'
    
    ALTER TABLE [archive].[DeploymentRequest]
    ADD [CancelledTime] DATETIMEOFFSET(7) NULL
    
    PRINT 'CancelledTime column added successfully'
END
ELSE
BEGIN
    PRINT 'CancelledTime column already exists in [archive].[DeploymentRequest]'
END
GO

-- Verify the columns were added
IF EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[archive].[DeploymentRequest]') 
    AND name IN ('CancelledBy', 'CancelledTime')
)
BEGIN
    PRINT 'Verification: Cancellation fields successfully added to [archive].[DeploymentRequest]'
    
    SELECT 
        c.name AS ColumnName,
        t.name AS DataType,
        c.max_length AS MaxLength,
        c.is_nullable AS IsNullable
    FROM sys.columns c
    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'[archive].[DeploymentRequest]')
    AND c.name IN ('CancelledBy', 'CancelledTime')
END
GO