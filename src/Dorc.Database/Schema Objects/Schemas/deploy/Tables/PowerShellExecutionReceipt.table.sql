CREATE TABLE [deploy].[PowerShellExecutionReceipt]
(
    [DeploymentResultId] INT NOT NULL,
    [ScriptOrdinal] INT NOT NULL,
    [DeploymentRequestId] INT NOT NULL,
    [ProjectId] INT NOT NULL,
    [ComponentId] INT NOT NULL,
    [DefinitionRevision] NVARCHAR(64) NOT NULL,
    [TextSha256] NVARCHAR(64) NOT NULL,
    [RuntimeVersion] NVARCHAR(64) NOT NULL,
    [RequestedRuntime] NVARCHAR(64) NOT NULL,
    [SourcePath] NVARCHAR(2048) NULL,
    [BuildIdentity] NVARCHAR(256) NULL,
    [Phase] NVARCHAR(32) NOT NULL,
    [PreparedAt] DATETIMEOFFSET NULL,
    [ObservedAt] DATETIMEOFFSET NOT NULL,
    [InvocationObservedAt] DATETIMEOFFSET NULL,
    [CompletedAt] DATETIMEOFFSET NULL,
    CONSTRAINT [PK_PowerShellExecutionReceipt] PRIMARY KEY ([DeploymentResultId], [ScriptOrdinal])
);
GO
CREATE INDEX [IX_PowerShellExecutionReceipt_Project] ON [deploy].[PowerShellExecutionReceipt] ([ProjectId], [ObservedAt], [DeploymentResultId]);
