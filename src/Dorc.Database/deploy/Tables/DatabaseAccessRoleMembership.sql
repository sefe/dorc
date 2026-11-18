CREATE TABLE [deploy].[DatabaseAccessRoleMembership] (
    [DatabaseId] INT NOT NULL,
    [PrincipalId] INT NOT NULL,
    [RoleId] INT NOT NULL,
    [Present] BIT NOT NULL,
    CONSTRAINT [PK_DatabaseAccessRoleMembership] PRIMARY KEY ([DatabaseId], [PrincipalId], [RoleId]),
    CONSTRAINT [FK_DatabaseAccessRoleMembership_PrincipalBinding] FOREIGN KEY ([DatabaseId], [PrincipalId]) REFERENCES [deploy].[DatabaseAccessPrincipalBinding] ([DatabaseId], [PrincipalId]),
    CONSTRAINT [FK_DatabaseAccessRoleMembership_RoleBinding] FOREIGN KEY ([DatabaseId], [RoleId]) REFERENCES [deploy].[DatabaseAccessRoleBinding] ([DatabaseId], [RoleId])
);
