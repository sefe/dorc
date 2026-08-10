-- Legacy-schema fixture for the tag migration test.
--
-- Recreates dbo.DATABASE and dbo.SERVER in their pre-migration shape, carrying the
-- delimited tag columns, and seeds the awkward values the migration has to survive:
-- multi-tag, padded, duplicate-within-a-value, empty entries, and NULL.
--
-- It also recreates the parts of the estate that surround those two tables, because
-- the migration's real risk is not the tag split — it is what has to happen around a
-- table being rebuilt:
--
--   * an incoming foreign key from a POPULATED child table. The publish must drop
--     EnvironmentDatabase_DATABASE_DB_ID_fk, rebuild the table, and re-add the
--     constraint as FK_EnvironmentDatabase_Database against deploy.Database(Id) —
--     with rows on both sides throughout. A fixture whose children are empty never
--     makes DacFx do that, and the failure would first appear in production.
--   * an outgoing foreign key to dbo.AD_GROUP, and the hand-created index
--     IX_DATABASE_Server_Name_DB_Name, both of which the rebuild has to carry over.
--   * an identity counter standing ABOVE the highest surviving row, which is what
--     the stash/restore pair exists for. Seeding a contiguous 10..40 would leave
--     IDENT_CURRENT equal to MAX(DB_ID), so the reseed branch would never run and
--     the test could not tell whether the pair works.

-------------------------------------------------------------------------------
-- Reference data the legacy tables depend on
-------------------------------------------------------------------------------
IF OBJECT_ID('dbo.[AD_GROUP]', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AD_GROUP] (
        [Group_ID]   INT IDENTITY(1,1) NOT NULL,
        [Group_Name] NVARCHAR(50) NULL,
        CONSTRAINT [AD_GROUP_Group_ID_pk] PRIMARY KEY CLUSTERED ([Group_ID] ASC) WITH (DATA_COMPRESSION = PAGE)
    );
END;

SET IDENTITY_INSERT [dbo].[AD_GROUP] ON;
INSERT INTO [dbo].[AD_GROUP] ([Group_ID], [Group_Name]) VALUES (1, N'DOrc Testers');
SET IDENTITY_INSERT [dbo].[AD_GROUP] OFF;

-------------------------------------------------------------------------------
-- dbo.DATABASE, as the estate has it
-------------------------------------------------------------------------------
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
        CONSTRAINT [PK_DATABASE] PRIMARY KEY CLUSTERED ([DB_ID] ASC) WITH (DATA_COMPRESSION = PAGE),
        CONSTRAINT [DATABASE_AD_GROUP_Group_ID_fk] FOREIGN KEY ([Group_ID]) REFERENCES [dbo].[AD_GROUP] ([Group_ID]),
        INDEX [IX_DATABASE_Server_Name_DB_Name] NONCLUSTERED ([Server_Name],[DB_Name])
    );
END;

SET IDENTITY_INSERT [dbo].[DATABASE] ON;
INSERT INTO [dbo].[DATABASE] ([DB_ID], [DB_Name], [DB_Type], [Server_Name], [Group_ID]) VALUES
    (10, N'MULTI_DB',     N'Endur;Reporting', N'srv-a', 1),     -- two tags
    (20, N'PADDED_DB',    N'  Endur  ',       N'srv-b', 1),     -- trailing/leading space
    (30, N'DUPES_DB',     N'Ops;Ops;;Ops',    N'srv-c', NULL),  -- duplicates and an empty entry
    (40, N'UNTAGGED_DB',  NULL,               N'srv-d', NULL),  -- no tags at all
    -- Deleted below. Its only job is to leave the identity counter at 500 while the
    -- highest surviving row is 40, which is the gap the stash/restore pair protects:
    -- without it the rebuild would hand Id 41..500 out a second time, and
    -- deploy.DatabaseAudit.DatabaseId still points at the originals.
    (500, N'DELETED_DB',  N'Ops',             N'srv-z', NULL);
SET IDENTITY_INSERT [dbo].[DATABASE] OFF;

DELETE FROM [dbo].[DATABASE] WHERE [DB_ID] = 500;

-------------------------------------------------------------------------------
-- dbo.SERVER, as the estate has it
-------------------------------------------------------------------------------
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

-------------------------------------------------------------------------------
-- Populated children holding foreign keys INTO the two tables being rebuilt.
-- These carry the legacy constraint names, so the publish has to drop them and
-- re-create them against deploy.Database(Id) / deploy.Server(Id).
-------------------------------------------------------------------------------
IF OBJECT_ID('deploy.[Environment]', 'U') IS NULL
BEGIN
    CREATE TABLE [deploy].[Environment] (
        [Id]                 INT              IDENTITY (1, 1) NOT NULL,
        [ObjectId]           UNIQUEIDENTIFIER CONSTRAINT [DF_Environment_ObjectId] DEFAULT (newsequentialid()) NOT NULL,
        [Name]               NVARCHAR (64)    NOT NULL,
        [Secure]             BIT              DEFAULT ((0)) NOT NULL,
        [IsProd]             BIT              DEFAULT ((0)) NOT NULL,
        [Owner]              NVARCHAR (100)   NULL,
        [ThinClientServer]   NVARCHAR (50)    NULL,
        [RestoredFromBackup] NVARCHAR (MAX)   NULL,
        [FileShare]          NVARCHAR (MAX)   NULL,
        [EnvNote]            NVARCHAR (MAX)   NULL,
        [Description]        NVARCHAR (MAX)   NULL,
        [LastUpdate]         DATETIME         NULL,
        [ParentId]           INT              NULL REFERENCES [deploy].[Environment] (Id),
        CONSTRAINT [PK_Environment] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [ParentId_not_itself] CHECK ([Id]!=[ParentId])
    );
END;

SET IDENTITY_INSERT [deploy].[Environment] ON;
INSERT INTO [deploy].[Environment] ([Id], [Name]) VALUES (7, N'Endur DV 07');
SET IDENTITY_INSERT [deploy].[Environment] OFF;

IF OBJECT_ID('deploy.[EnvironmentDatabase]', 'U') IS NULL
BEGIN
    CREATE TABLE [deploy].[EnvironmentDatabase] (
        [Id]    INT IDENTITY (1, 1) NOT NULL,
        [EnvId] INT NOT NULL,
        [DbId]  INT NOT NULL,
        CONSTRAINT [EnvironmentDatabase_DATABASE_DB_ID_fk] FOREIGN KEY ([DbId]) REFERENCES [dbo].[DATABASE] ([DB_ID]),
        CONSTRAINT [EnvironmentDatabase_Environment_EnvID_fk] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id])
    );
END;

INSERT INTO [deploy].[EnvironmentDatabase] ([EnvId], [DbId]) VALUES (7, 10), (7, 20), (7, 40);

IF OBJECT_ID('deploy.[EnvironmentServer]', 'U') IS NULL
BEGIN
    CREATE TABLE [deploy].[EnvironmentServer] (
        [Id]       INT IDENTITY (1, 1) NOT NULL,
        [EnvId]    INT NOT NULL,
        [ServerId] INT NOT NULL,
        CONSTRAINT [EnvironmentServer_Environment_Id_fk] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id]),
        CONSTRAINT [EnvironmentServer_SERVER_Server_ID_fk] FOREIGN KEY ([ServerId]) REFERENCES [dbo].[SERVER] ([Server_ID])
    );
END;

INSERT INTO [deploy].[EnvironmentServer] ([EnvId], [ServerId]) VALUES (7, 100), (7, 200);
