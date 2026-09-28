CREATE TABLE [deploy].[Server] (
    [Id]     INT             IDENTITY (1, 1) NOT NULL,
    [Name]   NVARCHAR (250)  NULL,
    [OsName] NVARCHAR (250)  NULL,
    -- DEPRECATED, removed in the follow-up release - see deploy.Database.Tags.
    [Tags]   NVARCHAR (4000) NULL,
    CONSTRAINT [PK_Server] PRIMARY KEY CLUSTERED ([Id] ASC) WITH (DATA_COMPRESSION = PAGE)
);
