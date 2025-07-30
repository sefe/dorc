-- Add ComponentType column to existing Component table if it doesn't exist
-- Default value 0 corresponds to PowerShell ComponentType
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('[deploy].[Component]') AND name = 'ComponentType')
BEGIN
    ALTER TABLE [deploy].[Component] ADD [ComponentType] INT NOT NULL DEFAULT 0;
    PRINT 'Added ComponentType column to Component table';
END
ELSE
BEGIN
    PRINT 'ComponentType column already exists in Component table';
END