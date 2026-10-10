CREATE TABLE [deploy].[ApiRegistration] (
    [Id]             INT            IDENTITY (1, 1) NOT NULL,
    [Name]           NVARCHAR (250) NOT NULL,
    [BaseUrl]        NVARCHAR (500) NOT NULL,
    [Version]        NVARCHAR (50)  NULL,
    [HealthCheckUrl] NVARCHAR (500) NULL,
    [Tags]           NVARCHAR (250) NULL,
    CONSTRAINT [PK_ApiRegistration] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UQ_ApiRegistration_Name]
    ON [deploy].[ApiRegistration] ([Name] ASC);
