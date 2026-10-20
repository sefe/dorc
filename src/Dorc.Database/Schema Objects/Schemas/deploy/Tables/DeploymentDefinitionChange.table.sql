CREATE TABLE [deploy].[DeploymentDefinitionChange]
(
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [ProjectId] INT NOT NULL,
    [ProjectName] NVARCHAR(256) NOT NULL,
    [OccurredAt] DATETIMEOFFSET NOT NULL,
    [Actor] NVARCHAR(256) NOT NULL,
    [Operation] NVARCHAR(64) NOT NULL,
    [BeforeJson] NVARCHAR(MAX) NOT NULL,
    [AfterJson] NVARCHAR(MAX) NOT NULL,
    [Revision] NVARCHAR(64) NOT NULL,
    [SourceRecordId] NVARCHAR(128) NULL,
    [AffectedComponentsJson] NVARCHAR(MAX) NULL
);
GO
CREATE INDEX [IX_DeploymentDefinitionChange_Project] ON [deploy].[DeploymentDefinitionChange] ([ProjectId], [OccurredAt], [Id]);
