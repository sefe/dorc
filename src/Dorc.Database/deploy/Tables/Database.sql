CREATE TABLE [deploy].[Database] (
    [Id]         INT             IDENTITY (1, 1) NOT NULL,
    [Name]       NVARCHAR (250)  NULL,
    -- DEPRECATED, removed in the follow-up release. deploy.DatabaseTag is the
    -- source of truth; this is dual-written so the column stays accurate for
    -- anything still reading it directly.
    --
    -- The reason for the overlap release is the external readers, not the
    -- deployment mechanics: operational tooling and direct-SQL consumers need a
    -- release in which both stores are correct so they can be moved across.
    --
    -- Note that deferring the drop does NOT make it easier. SqlPackage's
    -- data-loss guard fires on the table being non-empty when a column is
    -- dropped, and deploy.Database is never empty — so the follow-up release
    -- meets exactly the same guard this one would have. Dropping the column will
    -- need either BlockOnPossibleDataLoss=False or a pre-deployment ALTER TABLE
    -- ... DROP COLUMN, which removes it from the target before the model diff
    -- runs. Plan for that; it is not a free consequence of waiting.
    [Tags]       NVARCHAR (4000) NULL,
    [ServerName] NVARCHAR (250)  NULL,
    [ArrayName]  NVARCHAR (250)  NULL,
    [GroupId]    INT             NULL,
    CONSTRAINT [PK_Database] PRIMARY KEY CLUSTERED ([Id] ASC) WITH (DATA_COMPRESSION = PAGE),
    CONSTRAINT [FK_Database_AdGroup] FOREIGN KEY ([GroupId]) REFERENCES [dbo].[AD_GROUP] ([Group_ID]),
    INDEX [IX_Database_ServerName_Name] NONCLUSTERED ([ServerName], [Name]),
    -- This one is NEW, not carried over. DatabaseEntityTypeConfiguration declares it
    -- on the EF side, but sys.indexes on the estate holds only PK_DATABASE and
    -- IX_DATABASE_Server_Name_DB_Name, so no such index was ever created there. The
    -- publish creates it.
    --
    -- IX_Database_ServerName_Name above is the opposite case and is why the
    -- distinction matters: that one does exist in the estate, under its old name, and
    -- has to be renamed through the refactorlog rather than merely redeclared here —
    -- otherwise plan verification blocks the column renames it depends on (SQL72031).
    INDEX [IX_Database_GroupId] NONCLUSTERED ([GroupId])
);
