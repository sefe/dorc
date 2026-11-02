CREATE TABLE [deploy].[DatabaseAccessSharedPrincipal] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(128) NOT NULL,
    [Kind] NVARCHAR(32) NOT NULL,
    [LoginName] NVARCHAR(128) NULL,
    [DirectoryId] NVARCHAR(256) NULL,
    CONSTRAINT [PK_DatabaseAccessSharedPrincipal] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DatabaseAccessSharedPrincipal_Identity] CHECK (
        ([Kind] = N'Native' AND [LoginName] IS NOT NULL AND [DirectoryId] IS NULL) OR
        ([Kind] IN (N'DirectoryUser', N'DirectoryGroup') AND [LoginName] IS NULL AND [DirectoryId] IS NOT NULL))
);
GO
CREATE UNIQUE INDEX [UX_DatabaseAccessSharedPrincipal_Identity]
    ON [deploy].[DatabaseAccessSharedPrincipal] ([Kind], [Name], [LoginName], [DirectoryId]);
