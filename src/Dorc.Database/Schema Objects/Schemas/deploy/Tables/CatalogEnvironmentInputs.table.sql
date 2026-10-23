CREATE TABLE [deploy].[CatalogEnvironmentInputs]
(
    [ComponentId] INT NOT NULL,
    [EnvironmentId] INT NOT NULL,
    [TemplateVersion] NVARCHAR(64) NOT NULL,
    [Revision] BIGINT NOT NULL,
    [InputsJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_CatalogEnvironmentInputs] PRIMARY KEY ([ComponentId], [EnvironmentId])
);
