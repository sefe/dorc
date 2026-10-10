CREATE TABLE [dbo].[DatabaseAccessRole] (
    [DatabaseId] INT NOT NULL,
    [Name] NVARCHAR(128) NOT NULL,
    [Managed] BIT NOT NULL,
    [Present] BIT NOT NULL,
    CONSTRAINT [PK_DatabaseAccessRole] PRIMARY KEY ([DatabaseId], [Name]),
    CONSTRAINT [FK_DatabaseAccessRole_Configuration] FOREIGN KEY ([DatabaseId]) REFERENCES [dbo].[DatabaseAccessConfiguration] ([DatabaseId])
);
