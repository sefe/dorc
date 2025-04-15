CREATE TABLE [dbo].[SERVER] (
    [Server_ID]               INT           IDENTITY (1, 1) NOT NULL,
    [Server_Name]             NVARCHAR (250) NULL,
    [OS_Version]              NVARCHAR (250) NULL,
    [Application_Server_Name] NVARCHAR (1000) NULL,
    CONSTRAINT [PK_SERVER] PRIMARY KEY CLUSTERED ([Server_ID] ASC) WITH (DATA_COMPRESSION = PAGE)
);

