CREATE TABLE [archive].[DeploymentResultAttempt] (
    [Id]                          INT                NOT NULL,
    [DeploymentRequestAttemptId]  INT                NOT NULL,
    [ComponentId]                 INT                NOT NULL,
    [ComponentName]               NVARCHAR (256)     NOT NULL,
    [StartedTime]                 DATETIMEOFFSET (7) NULL,
    [CompletedTime]               DATETIMEOFFSET (7) NULL,
    [Status]                      NVARCHAR (32)      NOT NULL,
    [DeploymentResultId]          INT                NULL,
    [Log]                         NVARCHAR (MAX)     NULL
);
