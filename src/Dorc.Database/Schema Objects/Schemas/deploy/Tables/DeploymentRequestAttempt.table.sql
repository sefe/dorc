CREATE TABLE [deploy].[DeploymentRequestAttempt] (
    [Id]                  INT                IDENTITY (1, 1) NOT NULL,
    [DeploymentRequestId] INT                NOT NULL,
    [AttemptNumber]       INT                NOT NULL,
    [StartedTime]         DATETIMEOFFSET (7) NULL,
    [CompletedTime]       DATETIMEOFFSET (7) NULL,
    [Status]              NVARCHAR (32)      NOT NULL,
    [Log]                 NVARCHAR (MAX)     NULL,
    [UserName]            NVARCHAR (128)     NOT NULL,
    CONSTRAINT [PK_DeploymentRequestAttempt] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_DeploymentRequestAttempt_DeploymentRequest] FOREIGN KEY ([DeploymentRequestId]) REFERENCES [deploy].[DeploymentRequest] ([Id])
);

GO
CREATE NONCLUSTERED INDEX [IX_DeploymentRequestAttempt_DeploymentRequestId]
    ON [deploy].[DeploymentRequestAttempt]([DeploymentRequestId] ASC);