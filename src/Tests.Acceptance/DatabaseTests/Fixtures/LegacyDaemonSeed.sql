-- Legacy-schema fixture used by SC-02 migration test.
-- Recreates the pre-modernisation dbo.SERVICE / dbo.SERVER_SERVICE_MAP shape and inserts
-- representative rows (including one NULL-name row and one orphan-server row to exercise
-- the filters in the post-deploy migration script).

-- Ensure there's a server row so FK-valid mappings have a target.
IF OBJECT_ID('dbo.SERVER', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SERVER] (
        [Server_ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Server_Name] NVARCHAR(32) NULL
    );
END;

-- Insert a single test server. Id captured below.
INSERT INTO [dbo].[SERVER] (Server_Name) VALUES (N'test-server-01');
DECLARE @testServerId INT = SCOPE_IDENTITY();

-- Legacy dbo.SERVICE shape (pre-modernisation).
CREATE TABLE [dbo].[SERVICE] (
    [Service_ID]   INT            IDENTITY (1, 1) NOT NULL,
    [Service_Name] NVARCHAR (250) NULL,
    [Display_Name] NVARCHAR (250) NULL,
    [Account_Name] NVARCHAR (250) NULL,
    [Service_Type] NVARCHAR (250) NULL,
    CONSTRAINT [PK_SERVICE] PRIMARY KEY CLUSTERED ([Service_ID] ASC)
);

SET IDENTITY_INSERT [dbo].[SERVICE] ON;

INSERT INTO [dbo].[SERVICE] (Service_ID, Service_Name, Display_Name, Account_Name, Service_Type) VALUES
    (100, N'alpha-daemon', N'Alpha Daemon',  N'LocalSystem',            N'Windows Service'),
    (200, N'beta-daemon',  N'Beta Daemon',   N'DOMAIN\svc-beta',        N'Windows Service'),
    (300, N'gamma-daemon', N'Gamma Daemon',  N'LocalSystem',            N'Windows Service'),
    (400, NULL,            N'Nameless Row',  N'LocalSystem',            N'Windows Service'); -- exercises NULL-name filter

SET IDENTITY_INSERT [dbo].[SERVICE] OFF;

-- Legacy dbo.SERVER_SERVICE_MAP shape.
CREATE TABLE [dbo].[SERVER_SERVICE_MAP] (
    [Server_ID]  INT NOT NULL,
    [Service_ID] INT NOT NULL,
    CONSTRAINT [SERVER_SERVICE_MAP_SERVER_Server_ID_fk] FOREIGN KEY ([Server_ID]) REFERENCES [dbo].[SERVER] ([Server_ID]),
    CONSTRAINT [SERVER_SERVICE_MAP_SERVICE_Service_ID_fk] FOREIGN KEY ([Service_ID]) REFERENCES [dbo].[SERVICE] ([Service_ID])
);

INSERT INTO [dbo].[SERVER_SERVICE_MAP] (Server_ID, Service_ID) VALUES
    (@testServerId, 100),
    (@testServerId, 200),
    (@testServerId, 300);

-- Orphan map row: disable the FK temporarily so the orphan can be inserted for the migration
-- filter to exercise. The legacy production DB has FK_enabled rows only, so this is synthetic,
-- but the post-deploy filter still has to tolerate stale data that might exist.
ALTER TABLE [dbo].[SERVER_SERVICE_MAP] NOCHECK CONSTRAINT [SERVER_SERVICE_MAP_SERVER_Server_ID_fk];

INSERT INTO [dbo].[SERVER_SERVICE_MAP] (Server_ID, Service_ID) VALUES
    (99999, 100); -- orphan Server_ID — exercises orphan-filter in migration

ALTER TABLE [dbo].[SERVER_SERVICE_MAP] CHECK CONSTRAINT [SERVER_SERVICE_MAP_SERVER_Server_ID_fk];
