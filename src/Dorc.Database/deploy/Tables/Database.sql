CREATE TABLE [deploy].[Database] (
    [Id]         INT             IDENTITY (1, 1) NOT NULL,
    [Name]       NVARCHAR (250)  NULL,
    [Tags]       NVARCHAR (4000) NULL,
    [ServerName] NVARCHAR (250)  NULL,
    [ArrayName]  NVARCHAR (250)  NULL,
    [GroupId]    INT             NULL,
    CONSTRAINT [PK_Database] PRIMARY KEY CLUSTERED ([Id] ASC) WITH (DATA_COMPRESSION = PAGE),
    CONSTRAINT [FK_Database_AdGroup] FOREIGN KEY ([GroupId]) REFERENCES [dbo].[AD_GROUP] ([Group_ID]),
    INDEX [IX_Database_ServerName_Name] NONCLUSTERED ([ServerName], [Name])
);
