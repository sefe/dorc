CREATE TABLE [deploy].[CatalogRequestInputs]
(
    [DeploymentRequestId] INT NOT NULL,
    [ComponentId] INT NOT NULL,
    [TemplateName] NVARCHAR(256) NOT NULL,
    [TemplateVersion] NVARCHAR(64) NOT NULL,
    [ConfigurationRevision] BIGINT NOT NULL,
    [InputsJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_CatalogRequestInputs] PRIMARY KEY ([DeploymentRequestId], [ComponentId])
);
