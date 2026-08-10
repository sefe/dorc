CREATE TABLE [deploy].[Database] (
    [Id]         INT             IDENTITY (1, 1) NOT NULL,
    [Name]       NVARCHAR (250)  NULL,
    -- DEPRECATED, removed in the follow-up release. deploy.DatabaseTag is the
    -- source of truth; this is dual-written so the column stays accurate for
    -- anything still reading it directly. It cannot be dropped in the same
    -- release: SqlPackage's data-loss guard aborts the publish on any
    -- non-empty table when a column is dropped.
    [Tags]       NVARCHAR (4000) NULL,
    [ServerName] NVARCHAR (250)  NULL,
    [ArrayName]  NVARCHAR (250)  NULL,
    [GroupId]    INT             NULL,
    CONSTRAINT [PK_Database] PRIMARY KEY CLUSTERED ([Id] ASC) WITH (DATA_COMPRESSION = PAGE),
    CONSTRAINT [FK_Database_AdGroup] FOREIGN KEY ([GroupId]) REFERENCES [dbo].[AD_GROUP] ([Group_ID]),
    INDEX [IX_Database_ServerName_Name] NONCLUSTERED ([ServerName], [Name]),
    -- Modelled explicitly because the move rebuilds this table: the rebuild creates
    -- the replacement from the model alone and drops the original, so an index that
    -- exists only in the estate (this one was hand-created, and is declared by
    -- DatabaseEntityTypeConfiguration) would be silently lost.
    INDEX [IX_Database_GroupId] NONCLUSTERED ([GroupId])
);
