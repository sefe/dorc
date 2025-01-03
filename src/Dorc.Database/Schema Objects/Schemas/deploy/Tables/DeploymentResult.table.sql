CREATE TABLE [deploy].[DeploymentResult] (
    [Id]                  INT            IDENTITY (1, 1) NOT NULL,
    [DeploymentRequestId] INT            NOT NULL,
    [ComponentId]         INT            NOT NULL,
    [StartedTime]         DATETIMEOFFSET (7) NULL,
    [CompletedTime]       DATETIMEOFFSET (7) NULL,
    [Log]                 NVARCHAR (MAX) NULL,
    [Status]              NVARCHAR (32)  NOT NULL
);

