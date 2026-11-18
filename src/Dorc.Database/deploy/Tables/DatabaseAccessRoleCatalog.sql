CREATE TABLE [deploy].[DatabaseAccessRoleCatalog] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(128) NOT NULL,
    CONSTRAINT [PK_DatabaseAccessRoleCatalog] PRIMARY KEY ([Id]),
    CONSTRAINT [UX_DatabaseAccessRoleCatalog_Name] UNIQUE ([Name])
);
