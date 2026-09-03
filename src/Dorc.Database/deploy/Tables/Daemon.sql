CREATE TABLE [deploy].[Daemon] (
    [Id]          INT            IDENTITY (1, 1) NOT NULL,
    [Name]        NVARCHAR (250) NOT NULL,
    [DisplayName] NVARCHAR (250) NULL,
    [AccountName] NVARCHAR (250) NULL,
    [Type]        NVARCHAR (250) NULL,
    CONSTRAINT [PK_Daemon] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UQ_Daemon_Name]
    ON [deploy].[Daemon] ([Name] ASC);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UQ_Daemon_DisplayName]
    ON [deploy].[Daemon] ([DisplayName] ASC)
    WHERE [DisplayName] IS NOT NULL;
