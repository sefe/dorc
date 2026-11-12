CREATE TABLE [deploy].[DeploymentRequestDefinition]
(
    [DeploymentRequestId] INT NOT NULL,
    [Ordinal] INT NOT NULL,
    [ProjectId] INT NOT NULL,
    [ComponentId] INT NOT NULL,
    [ComponentName] NVARCHAR(256) NOT NULL,
    [DefinitionRevisionId] UNIQUEIDENTIFIER NOT NULL,
    [DefinitionRevision] NVARCHAR(64) NOT NULL,
    [DefinitionJson] NVARCHAR(MAX) NOT NULL,
    [ProjectJson] NVARCHAR(MAX) NOT NULL,
    [ScriptJson] NVARCHAR(MAX) NULL,
    [IsSkipped] BIT NOT NULL,
    [SubmittedAt] DATETIMEOFFSET NOT NULL,
    [SubmittedBy] NVARCHAR(256) NOT NULL,
    CONSTRAINT [PK_DeploymentRequestDefinition] PRIMARY KEY ([DeploymentRequestId], [Ordinal])
);
GO
CREATE INDEX [IX_DeploymentRequestDefinition_Project] ON [deploy].[DeploymentRequestDefinition] ([ProjectId], [SubmittedAt], [DeploymentRequestId]);
