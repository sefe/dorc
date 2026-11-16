CREATE TABLE [deploy].[CloudResource] (
    [Id]                 INT            IDENTITY (1, 1) NOT NULL,
    [Name]               NVARCHAR (250) NOT NULL,
    [Provider]           NVARCHAR (250) NOT NULL,
    [ResourceType]       NVARCHAR (250) NOT NULL,
    [ResourceIdentifier] NVARCHAR (500) NOT NULL,
    [Subscription]       NVARCHAR (250) NULL,
    [Tags]               NVARCHAR (250) NULL,
    CONSTRAINT [PK_CloudResource] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UQ_CloudResource_Name]
    ON [deploy].[CloudResource] ([Name] ASC);
