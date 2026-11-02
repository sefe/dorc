CREATE TABLE [deploy].[DatabaseAccessDatabaseMembership] (
    [DatabaseId] INT NOT NULL,
    [PrincipalId] INT NOT NULL,
    [RoleId] INT NOT NULL,
    [Present] BIT NOT NULL,
    CONSTRAINT [PK_DatabaseAccessDatabaseMembership] PRIMARY KEY ([DatabaseId], [PrincipalId], [RoleId]),
    CONSTRAINT [FK_DatabaseAccessDatabaseMembership_Principal] FOREIGN KEY ([DatabaseId], [PrincipalId]) REFERENCES [deploy].[DatabaseAccessDatabasePrincipal] ([DatabaseId], [PrincipalId]),
    CONSTRAINT [FK_DatabaseAccessDatabaseMembership_Role] FOREIGN KEY ([DatabaseId], [RoleId]) REFERENCES [deploy].[DatabaseAccessDatabaseRole] ([DatabaseId], [RoleId])
);
