-- Legacy-schema fixture for the tag migration test.
--
-- Recreates dbo.DATABASE and dbo.SERVER in their pre-migration shape, carrying the
-- delimited tag columns, and seeds the awkward values the migration has to survive:
-- multi-tag, padded, duplicate-within-a-value, empty entries, and NULL.

IF OBJECT_ID('dbo.[DATABASE]', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DATABASE] (
        [DB_ID]       INT IDENTITY(1,1) NOT NULL,
        [DB_Name]     NVARCHAR(250) NULL,
        [DB_Type]     NVARCHAR(250) NULL,
        [Server_Name] NVARCHAR(250) NULL,
        [Group_ID]    INT NULL,
        [Array_Name]  NVARCHAR(250) NULL,
        -- Named to match the estate: DacFx imports a system-named (inline) PK as an
        -- unnamed model element, which can never pair with the model's PK_Database -
        -- leaving a target-only constraint on the renamed Id column that fails plan
        -- verification with SQL72031. The real estate's PK is named, so the fixture
        -- must be too.
        CONSTRAINT [PK_DATABASE] PRIMARY KEY CLUSTERED ([DB_ID] ASC) WITH (DATA_COMPRESSION = PAGE)
    );
END;

SET IDENTITY_INSERT [dbo].[DATABASE] ON;
INSERT INTO [dbo].[DATABASE] ([DB_ID], [DB_Name], [DB_Type], [Server_Name]) VALUES
    (10, N'MULTI_DB',     N'Endur;Reporting', N'srv-a'),  -- two tags
    (20, N'PADDED_DB',    N'  Endur  ',       N'srv-b'),  -- trailing/leading space
    (30, N'DUPES_DB',     N'Ops;Ops;;Ops',    N'srv-c'),  -- duplicates and an empty entry
    (40, N'UNTAGGED_DB',  NULL,               N'srv-d');  -- no tags at all
SET IDENTITY_INSERT [dbo].[DATABASE] OFF;

IF OBJECT_ID('dbo.[SERVER]', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SERVER] (
        [Server_ID]               INT IDENTITY(1,1) NOT NULL,
        [Server_Name]             NVARCHAR(250) NULL,
        [OS_Version]              NVARCHAR(250) NULL,
        [Application_Server_Name] NVARCHAR(1000) NULL,
        -- Named for the same SQL72031 reason as PK_DATABASE above.
        CONSTRAINT [PK_SERVER] PRIMARY KEY CLUSTERED ([Server_ID] ASC) WITH (DATA_COMPRESSION = PAGE)
    );
END;

SET IDENTITY_INSERT [dbo].[SERVER] ON;
INSERT INTO [dbo].[SERVER] ([Server_ID], [Server_Name], [Application_Server_Name]) VALUES
    (100, N'app01', N'appserver-node;WebServer'),
    (200, N'web01', NULL);
SET IDENTITY_INSERT [dbo].[SERVER] OFF;
