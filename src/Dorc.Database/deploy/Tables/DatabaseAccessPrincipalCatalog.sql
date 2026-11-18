CREATE TABLE [deploy].[DatabaseAccessPrincipalCatalog] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(128) NOT NULL,
    [Kind] NVARCHAR(32) NOT NULL,
    [LoginName] NVARCHAR(128) NULL,
    [DirectoryId] NVARCHAR(256) NULL,
    CONSTRAINT [PK_DatabaseAccessPrincipalCatalog] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DatabaseAccessPrincipalCatalog_Identity] CHECK (
        ([Kind] = N'Native' AND [LoginName] IS NOT NULL AND [DirectoryId] IS NULL) OR
        ([Kind] IN (N'DirectoryUser', N'DirectoryGroup') AND [LoginName] IS NULL AND [DirectoryId] IS NOT NULL))
);
GO
CREATE UNIQUE INDEX [UX_DatabaseAccessPrincipalCatalog_Identity]
    ON [deploy].[DatabaseAccessPrincipalCatalog] ([Kind], [Name], [LoginName], [DirectoryId]);
