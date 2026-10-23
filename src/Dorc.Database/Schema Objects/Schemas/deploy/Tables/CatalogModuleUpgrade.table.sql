CREATE TABLE [deploy].[CatalogModuleUpgrade]
(
    [ComponentId] INT NOT NULL,
    [Revision] BIGINT NOT NULL,
    [FromVersion] NVARCHAR(64) NOT NULL,
    [ToVersion] NVARCHAR(64) NOT NULL,
    [UpdatedBy] NVARCHAR(256) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET NOT NULL,
    CONSTRAINT [PK_CatalogModuleUpgrade] PRIMARY KEY ([ComponentId], [Revision])
);
