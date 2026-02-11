CREATE TABLE [deploy].[DeploymentResultAttempt] (
    [Id]                          INT                IDENTITY (1, 1) NOT NULL,
    [DeploymentRequestAttemptId]  INT                NOT NULL,
    [ComponentId]                 INT                NOT NULL,
    [ComponentName]               NVARCHAR (256)     NOT NULL,
    [StartedTime]                 DATETIMEOFFSET (7) NULL,
    [CompletedTime]               DATETIMEOFFSET (7) NULL,
    [Status]                      NVARCHAR (32)      NOT NULL,
    [Log]                         NVARCHAR (MAX)     NULL,
    CONSTRAINT [PK_DeploymentResultAttempt] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_DeploymentResultAttempt_DeploymentRequestAttempt] FOREIGN KEY ([DeploymentRequestAttemptId]) REFERENCES [deploy].[DeploymentRequestAttempt] ([Id]) ON DELETE CASCADE
);

GO
CREATE NONCLUSTERED INDEX [IX_DeploymentResultAttempt_DeploymentRequestAttemptId]
    ON [deploy].[DeploymentResultAttempt]([DeploymentRequestAttemptId] ASC);