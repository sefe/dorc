CREATE TABLE [deploy].[DatabaseAccessSharedRole] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(128) NOT NULL,
    CONSTRAINT [PK_DatabaseAccessSharedRole] PRIMARY KEY ([Id]),
    CONSTRAINT [UX_DatabaseAccessSharedRole_Name] UNIQUE ([Name])
);
