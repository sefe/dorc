CREATE TABLE [deploy].[DatabaseAccessDatabaseRole] (
    [DatabaseId] INT NOT NULL,
    [RoleId] INT NOT NULL,
    [Managed] BIT NOT NULL,
    [Present] BIT NOT NULL,
    CONSTRAINT [PK_DatabaseAccessDatabaseRole] PRIMARY KEY ([DatabaseId], [RoleId]),
    CONSTRAINT [FK_DatabaseAccessDatabaseRole_Configuration] FOREIGN KEY ([DatabaseId]) REFERENCES [deploy].[DatabaseAccessConfiguration] ([DatabaseId]),
    CONSTRAINT [FK_DatabaseAccessDatabaseRole_SharedRole] FOREIGN KEY ([RoleId]) REFERENCES [deploy].[DatabaseAccessSharedRole] ([Id])
);
