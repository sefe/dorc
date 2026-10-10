CREATE TABLE [dbo].[DatabaseAccessPrincipal] (
    [DatabaseId] INT NOT NULL,
    [Name] NVARCHAR(128) NOT NULL,
    [Kind] NVARCHAR(32) NOT NULL,
    [LoginName] NVARCHAR(128) NULL,
    [DirectoryId] NVARCHAR(256) NULL,
    [Present] BIT NOT NULL,
    [LegacyUserId] INT NULL,
    CONSTRAINT [PK_DatabaseAccessPrincipal] PRIMARY KEY ([DatabaseId], [Name]),
    CONSTRAINT [FK_DatabaseAccessPrincipal_Configuration] FOREIGN KEY ([DatabaseId]) REFERENCES [dbo].[DatabaseAccessConfiguration] ([DatabaseId]),
    CONSTRAINT [CK_DatabaseAccessPrincipal_Identity] CHECK (
        ([Kind] = N'Native' AND [LoginName] IS NOT NULL AND [DirectoryId] IS NULL) OR
        ([Kind] IN (N'DirectoryUser', N'DirectoryGroup') AND [LoginName] IS NULL AND [DirectoryId] IS NOT NULL))
);
