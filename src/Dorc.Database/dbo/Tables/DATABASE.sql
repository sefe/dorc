CREATE TABLE [dbo].[DATABASE] (
    [DB_ID]        INT           IDENTITY (1, 1) NOT NULL,
    [DB_Name]      NVARCHAR (50) NULL,
    [DB_Type]      NVARCHAR (50) NULL,
    [Server_Name]  NVARCHAR (50) NULL,
    [Group_ID]     INT           NULL,
	[Array_Name]   NVARCHAR (50) NULL,
    CONSTRAINT [PK_DATABASE] PRIMARY KEY CLUSTERED ([DB_ID] ASC) WITH (DATA_COMPRESSION = PAGE),
    CONSTRAINT [DATABASE_AD_GROUP_Group_ID_fk] FOREIGN KEY ([Group_ID]) REFERENCES [dbo].[AD_GROUP] ([Group_ID]),
    INDEX [IX_DATABASE_Server_Name_DB_Name] NONCLUSTERED ([Server_Name],[DB_Name])
);
