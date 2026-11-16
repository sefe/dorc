CREATE TABLE [deploy].[DeploymentDefinitionRevision]
(
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [ChangeId] UNIQUEIDENTIFIER NULL,
    [ProjectId] INT NOT NULL,
    [ComponentId] INT NOT NULL,
    [ComponentName] NVARCHAR(256) NOT NULL,
    [DefinitionJson] NVARCHAR(MAX) NOT NULL,
    [Revision] NVARCHAR(64) NOT NULL,
    [ObservedAt] DATETIMEOFFSET NOT NULL,
    [IsBaseline] BIT NOT NULL
);
GO
CREATE UNIQUE INDEX [IX_DeploymentDefinitionRevision_Identity] ON [deploy].[DeploymentDefinitionRevision] ([ProjectId], [ComponentId], [Revision]);
