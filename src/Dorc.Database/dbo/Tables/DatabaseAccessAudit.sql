CREATE TABLE [dbo].[DatabaseAccessAudit] (
    [Id] BIGINT IDENTITY(1,1) NOT NULL,
    [DatabaseId] INT NOT NULL,
    [CreatedUtc] DATETIME2 NOT NULL,
    [Actor] NVARCHAR(512) NOT NULL,
    [Action] NVARCHAR(32) NOT NULL,
    [Status] NVARCHAR(32) NOT NULL,
    [Detail] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_DatabaseAccessAudit] PRIMARY KEY ([Id])
);
GO
CREATE INDEX [IX_DatabaseAccessAudit_DatabaseId_CreatedUtc] ON [dbo].[DatabaseAccessAudit] ([DatabaseId], [CreatedUtc]);
