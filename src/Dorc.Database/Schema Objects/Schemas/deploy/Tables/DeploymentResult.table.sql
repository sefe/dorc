CREATE TABLE [deploy].[DeploymentResult] (
    [Id]                  INT            IDENTITY (1, 1) NOT NULL,
    [DeploymentRequestId] INT            NOT NULL,
    [ComponentId]         INT            NOT NULL,
    [Log]                 NVARCHAR (MAX) NULL,
    [Status]              NVARCHAR (32)  NOT NULL
);

