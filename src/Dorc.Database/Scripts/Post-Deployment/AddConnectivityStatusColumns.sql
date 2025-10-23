-- Add connectivity status columns to SERVER table if they don't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SERVER]') AND name = 'LastChecked')
BEGIN
    ALTER TABLE [dbo].[SERVER] ADD [LastChecked] DATETIME2 NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SERVER]') AND name = 'IsReachable')
BEGIN
    ALTER TABLE [dbo].[SERVER] ADD [IsReachable] BIT NULL;
END

-- Add connectivity status columns to DATABASE table if they don't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[DATABASE]') AND name = 'LastChecked')
BEGIN
    ALTER TABLE [dbo].[DATABASE] ADD [LastChecked] DATETIME2 NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[DATABASE]') AND name = 'IsReachable')
BEGIN
    ALTER TABLE [dbo].[DATABASE] ADD [IsReachable] BIT NULL;
END

PRINT 'Connectivity status columns added to SERVER and DATABASE tables'
GO
