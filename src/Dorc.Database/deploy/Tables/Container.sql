CREATE TABLE [deploy].[Container] (
    [Id]             INT            IDENTITY (1, 1) NOT NULL,
    [Name]           NVARCHAR (250) NOT NULL,
    [Image]          NVARCHAR (500) NOT NULL,
    [Registry]       NVARCHAR (250) NULL,
    [HostServerName] NVARCHAR (250) NULL,
    [Tags]           NVARCHAR (250) NULL,
    CONSTRAINT [PK_Container] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UQ_Container_Name]
    ON [deploy].[Container] ([Name] ASC);
