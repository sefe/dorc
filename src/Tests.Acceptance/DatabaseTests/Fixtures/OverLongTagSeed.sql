-- Fixture for the over-long tag guard.
--
-- The legacy dbo.SERVER.Application_Server_Name allowed 1000 characters while
-- deploy.ServerTag.Tag allows 400, so a single tag CAN be too long to migrate. The
-- release refuses to start rather than truncating it or dropping it, because either
-- would quietly change which servers a deployment resolves to.
--
-- 500 characters: over the 400 limit, inside the legacy column's 1000, and not a
-- delimited list — so it cannot be dismissed as a long value that splits into short
-- tags.

IF OBJECT_ID('dbo.[SERVER]', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SERVER] (
        [Server_ID]               INT IDENTITY(1,1) NOT NULL,
        [Server_Name]             NVARCHAR(250) NULL,
        [OS_Version]              NVARCHAR(250) NULL,
        [Application_Server_Name] NVARCHAR(1000) NULL,
        CONSTRAINT [PK_SERVER] PRIMARY KEY CLUSTERED ([Server_ID] ASC) WITH (DATA_COMPRESSION = PAGE)
    );
END;

SET IDENTITY_INSERT [dbo].[SERVER] ON;
INSERT INTO [dbo].[SERVER] ([Server_ID], [Server_Name], [Application_Server_Name]) VALUES
    (100, N'app01', REPLICATE(N'x', 500));
SET IDENTITY_INSERT [dbo].[SERVER] OFF;
