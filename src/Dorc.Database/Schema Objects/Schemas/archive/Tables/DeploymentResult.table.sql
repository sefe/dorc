CREATE TABLE [archive].[DeploymentResult] (
    [Id]                  INT            NOT NULL,
    [DeploymentRequestId] INT            NOT NULL,
    [ComponentId]         INT            NOT NULL,
    [Log]                 NVARCHAR (MAX) NULL,
    [Status]              NVARCHAR (32)  NOT NULL,
    [StartedTime]         DATETIMEOFFSET (7) NULL,
    [CompletedTime]       DATETIMEOFFSET (7) NULL
);
