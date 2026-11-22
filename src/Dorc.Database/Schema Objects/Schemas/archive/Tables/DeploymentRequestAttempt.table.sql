CREATE TABLE [archive].[DeploymentRequestAttempt] (
    [Id]                  INT                NOT NULL,
    [DeploymentRequestId] INT                NOT NULL,
    [AttemptNumber]       INT                NOT NULL,
    [StartedTime]         DATETIMEOFFSET (7) NULL,
    [CompletedTime]       DATETIMEOFFSET (7) NULL,
    [Status]              NVARCHAR (32)      NOT NULL,
    [Log]                 NVARCHAR (MAX)     NULL,
    [UserName]            NVARCHAR (128)     NOT NULL
);
