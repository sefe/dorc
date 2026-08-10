CREATE TABLE [deploy].[ServerTag] (
    [ServerId] INT            NOT NULL,
    [Tag]      NVARCHAR (200) NOT NULL,
    CONSTRAINT [PK_ServerTag] PRIMARY KEY CLUSTERED ([ServerId] ASC, [Tag] ASC),
    CONSTRAINT [FK_ServerTag_Server] FOREIGN KEY ([ServerId]) REFERENCES [deploy].[Server] ([Id]) ON DELETE CASCADE,
    INDEX [IX_ServerTag_Tag] NONCLUSTERED ([Tag] ASC) INCLUDE ([ServerId])
);
