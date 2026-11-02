CREATE TABLE [deploy].[DatabaseAccessExclusion] (
    [DatabaseId] INT NOT NULL,
    [LegacyUserId] INT NOT NULL,
    CONSTRAINT [PK_DatabaseAccessExclusion] PRIMARY KEY ([DatabaseId], [LegacyUserId]),
    CONSTRAINT [FK_DatabaseAccessExclusion_Configuration] FOREIGN KEY ([DatabaseId]) REFERENCES [deploy].[DatabaseAccessConfiguration] ([DatabaseId]),
    CONSTRAINT [CK_DatabaseAccessExclusion_LegacyUserId] CHECK ([LegacyUserId] > 0)
);
