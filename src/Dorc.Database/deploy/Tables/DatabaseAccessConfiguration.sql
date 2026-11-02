CREATE TABLE [deploy].[DatabaseAccessConfiguration] (
    [DatabaseId] INT NOT NULL,
    [Provider] NVARCHAR(40) NOT NULL,
    [Revision] BIGINT NOT NULL,
    CONSTRAINT [PK_DatabaseAccessConfiguration] PRIMARY KEY ([DatabaseId]),
    CONSTRAINT [FK_DatabaseAccessConfiguration_Database] FOREIGN KEY ([DatabaseId]) REFERENCES [deploy].[Database] ([Id]),
    CONSTRAINT [CK_DatabaseAccessConfiguration_Revision] CHECK ([Revision] >= 0)
);
