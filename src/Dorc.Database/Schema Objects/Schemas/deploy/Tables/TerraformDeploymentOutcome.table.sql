CREATE TABLE [deploy].[TerraformDeploymentOutcome]
(
    [DeploymentResultId] INT NOT NULL,
    [Revision] BIGINT NOT NULL,
    [ApplyOutcome] NVARCHAR(32) NOT NULL,
    [InventoryOutcome] NVARCHAR(64) NOT NULL,
    [ArchiveOutcome] NVARCHAR(32) NOT NULL,
    [RegisteredCount] INT NOT NULL,
    [FailedCount] INT NOT NULL,
    [AppliedAt] DATETIMEOFFSET NULL,
    [InventoryArtifactSha256] VARCHAR(64) NULL,
    [InventoryWarningsJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_TerraformDeploymentOutcome] PRIMARY KEY ([DeploymentResultId])
);
