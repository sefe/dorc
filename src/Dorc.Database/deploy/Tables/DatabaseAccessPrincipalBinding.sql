CREATE TABLE [deploy].[DatabaseAccessPrincipalBinding] (
    [DatabaseId] INT NOT NULL,
    [PrincipalId] INT NOT NULL,
    [Present] BIT NOT NULL,
    [LegacyUserId] INT NULL,
    CONSTRAINT [PK_DatabaseAccessPrincipalBinding] PRIMARY KEY ([DatabaseId], [PrincipalId]),
    CONSTRAINT [FK_DatabaseAccessPrincipalBinding_Configuration] FOREIGN KEY ([DatabaseId]) REFERENCES [deploy].[DatabaseAccessConfiguration] ([DatabaseId]),
    CONSTRAINT [FK_DatabaseAccessPrincipalBinding_PrincipalCatalog] FOREIGN KEY ([PrincipalId]) REFERENCES [deploy].[DatabaseAccessPrincipalCatalog] ([Id])
);
