CREATE TABLE [dbo].[DatabaseAccessMembership] (
    [DatabaseId] INT NOT NULL,
    [Principal] NVARCHAR(128) NOT NULL,
    [Role] NVARCHAR(128) NOT NULL,
    [Present] BIT NOT NULL,
    CONSTRAINT [PK_DatabaseAccessMembership] PRIMARY KEY ([DatabaseId], [Principal], [Role]),
    CONSTRAINT [FK_DatabaseAccessMembership_Principal] FOREIGN KEY ([DatabaseId], [Principal]) REFERENCES [dbo].[DatabaseAccessPrincipal] ([DatabaseId], [Name]),
    CONSTRAINT [FK_DatabaseAccessMembership_Role] FOREIGN KEY ([DatabaseId], [Role]) REFERENCES [dbo].[DatabaseAccessRole] ([DatabaseId], [Name])
);
