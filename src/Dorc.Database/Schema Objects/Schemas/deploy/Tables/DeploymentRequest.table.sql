CREATE TABLE [deploy].[DeploymentRequest] (
    [Id]             INT                IDENTITY (1, 1) NOT NULL,
    [RequestDetails] XML                NOT NULL,
    [UserName]       NVARCHAR (128)     NOT NULL,
    [RequestedTime]  DATETIMEOFFSET (7) NULL,
    [StartedTime]    DATETIMEOFFSET (7) NULL,
    [CompletedTime]  DATETIMEOFFSET (7) NULL,
    [Status]         NVARCHAR (32)      NOT NULL,
    [Log]            NVARCHAR (MAX)     NULL,
    [IsProd]		 bit                NOT NULL DEFAULT(0),
    [Project]        NVARCHAR (64)      NULL,
    [Environment]    NVARCHAR (64)      NULL,
    [BuildNumber]    NVARCHAR (256)     NULL,
    [Components]     NVARCHAR (MAX)     NULL,
    [UNCLogPath]     NVARCHAR (1024)    NULL,
    [ParentRequestId] INT               NULL,
    CONSTRAINT [PK_DeploymentRequest] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_DeploymentRequest_ParentRequest] FOREIGN KEY ([ParentRequestId]) REFERENCES [deploy].[DeploymentRequest]([Id])
);

GO
CREATE NONCLUSTERED INDEX [IX_Status_IsProd]
    ON [deploy].[DeploymentRequest]([Status] ASC, [IsProd] ASC);

GO
CREATE NONCLUSTERED INDEX [IX_ParentRequestId]
    ON [deploy].[DeploymentRequest]([ParentRequestId] ASC);

