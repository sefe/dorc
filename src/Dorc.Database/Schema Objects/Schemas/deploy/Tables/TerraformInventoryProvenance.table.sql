CREATE TABLE [deploy].[TerraformInventoryProvenance]
(
    [ComponentId] INT NOT NULL,
    [EnvironmentId] INT NOT NULL,
    [CloudResourceId] INT NOT NULL,
    [DatabaseId] INT NULL,
    [FirstSeenResultId] INT NOT NULL,
    [LastSeenResultId] INT NOT NULL,
    [LastSeenAt] DATETIMEOFFSET NOT NULL,
    [ResourceIdentifier] NVARCHAR(500) NOT NULL,
    [ReconciliationState] NVARCHAR(32) NOT NULL,
    [Revision] BIGINT NOT NULL,
    CONSTRAINT [PK_TerraformInventoryProvenance] PRIMARY KEY ([ComponentId], [EnvironmentId], [CloudResourceId])
);
