CREATE TABLE [deploy].[EnvironmentComponentStatus] (
    [Id]                  INT                IDENTITY (1, 1) NOT NULL,
    [EnvironmentId]       INT                NOT NULL,
    [ComponentId]         INT                NOT NULL,
    [Status]              NVARCHAR (32)      NOT NULL,
    [UpdateDate]          DATETIMEOFFSET (7) NOT NULL,
    [DeploymentRequestId] INT                NOT NULL
);

