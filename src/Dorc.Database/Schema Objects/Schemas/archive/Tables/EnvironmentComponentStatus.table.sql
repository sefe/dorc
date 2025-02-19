CREATE TABLE [archive].[EnvironmentComponentStatus] (
    [Id]                  INT                NOT NULL,
    [EnvironmentId]       INT                NOT NULL,
    [ComponentId]         INT                NOT NULL,
    [Status]              NVARCHAR (32)      NOT NULL,
    [UpdateDate]          DATETIMEOFFSET (7) NOT NULL,
    [DeploymentRequestId] INT                NOT NULL
);
