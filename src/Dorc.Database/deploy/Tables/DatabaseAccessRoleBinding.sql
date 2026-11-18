CREATE TABLE [deploy].[DatabaseAccessRoleBinding] (
    [DatabaseId] INT NOT NULL,
    [RoleId] INT NOT NULL,
    [Managed] BIT NOT NULL,
    [Present] BIT NOT NULL,
    CONSTRAINT [PK_DatabaseAccessRoleBinding] PRIMARY KEY ([DatabaseId], [RoleId]),
    CONSTRAINT [FK_DatabaseAccessRoleBinding_Configuration] FOREIGN KEY ([DatabaseId]) REFERENCES [deploy].[DatabaseAccessConfiguration] ([DatabaseId]),
    CONSTRAINT [FK_DatabaseAccessRoleBinding_RoleCatalog] FOREIGN KEY ([RoleId]) REFERENCES [deploy].[DatabaseAccessRoleCatalog] ([Id])
);
