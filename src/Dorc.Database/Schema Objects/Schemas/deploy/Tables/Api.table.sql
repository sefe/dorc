CREATE TABLE [deploy].[Api] (
    [Id]              INT            IDENTITY (1, 1) NOT NULL,
    [EnvId]           INT            NOT NULL,
    [Name]            NVARCHAR (128) NOT NULL,
    [Endpoint]        NVARCHAR (1024) NOT NULL,
    [Description]     NVARCHAR (MAX) NULL,
    [Type]            NVARCHAR (16)  NOT NULL,
    [AuthType]        NVARCHAR (16)  NOT NULL,
    [HealthCheckPath] NVARCHAR (512) NULL,
    [OwnerProjectId]  INT            NULL,
    [Tags]            NVARCHAR (512) NULL,
    CONSTRAINT [PK_Api] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Api_Environment] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Api_Project] FOREIGN KEY ([OwnerProjectId]) REFERENCES [deploy].[Project] ([Id]),
    CONSTRAINT [CK_Api_Type] CHECK ([Type] IN (N'REST', N'SOAP', N'gRPC')),
    CONSTRAINT [CK_Api_AuthType] CHECK ([AuthType] IN (N'None', N'Basic', N'Bearer', N'OAuth')),
    CONSTRAINT [UQ_Api_EnvId_Name] UNIQUE ([EnvId], [Name])
);
GO

CREATE NONCLUSTERED INDEX [IX_Api_EnvId]
    ON [deploy].[Api] ([EnvId] ASC);
