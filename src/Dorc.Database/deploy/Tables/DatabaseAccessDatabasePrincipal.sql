CREATE TABLE [deploy].[DatabaseAccessDatabasePrincipal] (
    [DatabaseId] INT NOT NULL,
    [PrincipalId] INT NOT NULL,
    [Present] BIT NOT NULL,
    [LegacyUserId] INT NULL,
    CONSTRAINT [PK_DatabaseAccessDatabasePrincipal] PRIMARY KEY ([DatabaseId], [PrincipalId]),
    CONSTRAINT [FK_DatabaseAccessDatabasePrincipal_Configuration] FOREIGN KEY ([DatabaseId]) REFERENCES [deploy].[DatabaseAccessConfiguration] ([DatabaseId]),
    CONSTRAINT [FK_DatabaseAccessDatabasePrincipal_SharedPrincipal] FOREIGN KEY ([PrincipalId]) REFERENCES [deploy].[DatabaseAccessSharedPrincipal] ([Id])
);
